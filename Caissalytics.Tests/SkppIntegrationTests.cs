using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caissalytics.Data;
using Xunit;

namespace Caissalytics.Tests;

public class SkppIntegrationTests : IDisposable
{
    private readonly string _testDir;

    public SkppIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "caissalytics_skpp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> HandlerFunc { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return HandlerFunc(request);
        }
    }

    [Fact]
    public async Task SkppService_SavesAndRetrievesDynamicApiUrl()
    {
        var service = new SkppIntegrationService(storageDirectory: _testDir);
        var initial = await service.GetConnectionStateAsync();
        Assert.Equal(string.Empty, initial.ApiUrl);
        Assert.False(initial.IsConnected);

        await service.SaveApiUrlAsync("http://localhost:5000/");

        var updated = await service.GetConnectionStateAsync();
        Assert.Equal("http://localhost:5000", updated.ApiUrl); // Trailing slash cleaned

        // Verify across new service instance reading disk
        var reloaded = new SkppIntegrationService(storageDirectory: _testDir);
        var state = await reloaded.GetConnectionStateAsync();
        Assert.Equal("http://localhost:5000", state.ApiUrl);
    }

    [Fact]
    public async Task SkppService_ConnectAsync_Success_StoresTokensAndNeverStoresPassword()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("http://localhost:5000/api/auth/token", req.RequestUri!.ToString());

                var responsePayload = new SkppTokenResponse
                {
                    AccessToken = "mock-jwt-access-token",
                    RefreshToken = "mock-refresh-token-123",
                    TokenType = "Bearer",
                    ExpiresIn = 900,
                    User = new SkppUserDto
                    {
                        Id = "u-101",
                        UserName = "tomas@skpp.sk",
                        Email = "tomas@skpp.sk",
                        FullName = "Tomáš Tréner",
                        Role = "coach",
                        FideId = 14912345,
                        CurrentClassical = 1950,
                        CurrentRapid = 1880,
                        Groups = new()
                        {
                            new SkppCoachingGroupDto
                            {
                                Id = 1,
                                Name = "Turnajová skupina",
                                Venue = "Klubovňa ŠKPP"
                            }
                        }
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        var (success, error) = await service.ConnectAsync("http://localhost:5000", "tomas@skpp.sk", "SecretPassword99!");

        Assert.True(success);
        Assert.Null(error);

        var state = await service.GetConnectionStateAsync();
        Assert.True(state.IsConnected);
        Assert.NotNull(state.User);
        Assert.Equal("coach", state.User.Role);
        Assert.True(state.User.IsCoach);
        Assert.False(state.User.IsStudent);
        Assert.Equal("Tomáš Tréner", state.User.FullName);
        Assert.Single(state.User.Groups);

        // Verify password is NEVER written to the session file
        string sessionFilePath = Path.Combine(_testDir, "skpp_session.json");
        Assert.True(File.Exists(sessionFilePath));
        string savedJson = await File.ReadAllTextAsync(sessionFilePath);
        Assert.DoesNotContain("SecretPassword99", savedJson);
        Assert.Contains("mock-jwt-access-token", savedJson);
        Assert.Contains("mock-refresh-token-123", savedJson);
    }

    [Fact]
    public async Task SkppService_ConnectAsync_RejectsInvalidCredentials()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"error\":\"Nesprávne prihlasovacie údaje alebo neaktívny účet.\"}", Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        var (success, error) = await service.ConnectAsync("http://localhost:5000", "baduser", "wrongpassword");

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Nesprávne prihlasovacie údaje", error);

        var state = await service.GetConnectionStateAsync();
        Assert.False(state.IsConnected);
    }

    [Fact]
    public async Task SkppService_ConnectAsync_RejectsNonCoachOrStudentRole()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                var responsePayload = new SkppTokenResponse
                {
                    AccessToken = "mock-access",
                    RefreshToken = "mock-refresh",
                    ExpiresIn = 900,
                    User = new SkppUserDto
                    {
                        Id = "u-999",
                        FullName = "Guest User",
                        Role = "member" // Not coach and not student
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        var (success, error) = await service.ConnectAsync("http://localhost:5000", "guest@skpp.sk", "password");

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Coach or Student", error);

        var state = await service.GetConnectionStateAsync();
        Assert.False(state.IsConnected);
    }

    [Fact]
    public async Task SkppService_DisconnectAsync_RevokesTokenAndClearsSession()
    {
        bool revokeCalled = false;
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/api/auth/token"))
                {
                    var responsePayload = new SkppTokenResponse
                    {
                        AccessToken = "token-to-revoke",
                        RefreshToken = "refresh-to-revoke",
                        ExpiresIn = 900,
                        User = new SkppUserDto { Role = "student", FullName = "Student 1" }
                    };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                    });
                }
                if (req.RequestUri.AbsolutePath.Contains("/api/auth/revoke"))
                {
                    revokeCalled = true;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        await service.ConnectAsync("http://localhost:5000", "student@skpp.sk", "Password123");
        var connectedState = await service.GetConnectionStateAsync();
        Assert.True(connectedState.IsConnected);

        await service.DisconnectAsync();

        Assert.True(revokeCalled);
        var disconnectedState = await service.GetConnectionStateAsync();
        Assert.False(disconnectedState.IsConnected);
        Assert.Null(disconnectedState.User);
        Assert.Equal("http://localhost:5000", disconnectedState.ApiUrl); // API URL preserved
    }

    [Fact]
    public async Task SkppService_VerifyAndRefreshProfileAsync_FetchesProfileWithBearerToken()
    {
        bool profileEndpointCalled = false;
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/api/auth/token"))
                {
                    var responsePayload = new SkppTokenResponse
                    {
                        AccessToken = "valid-bearer-token",
                        RefreshToken = "refresh-token-active",
                        ExpiresIn = 900,
                        User = new SkppUserDto { Role = "student", FullName = "Original Name" }
                    };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                    });
                }
                if (req.RequestUri.AbsolutePath.Contains("/api/caissalytics/profile"))
                {
                    profileEndpointCalled = true;
                    Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                    Assert.Equal("valid-bearer-token", req.Headers.Authorization?.Parameter);

                    var profileDto = new SkppUserDto
                    {
                        Id = "u-student",
                        FullName = "Updated Student Name",
                        Role = "student",
                        FideId = 14943514,
                        CurrentClassical = 1660,
                        CurrentRapid = 1625
                    };

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(profileDto), Encoding.UTF8, "application/json")
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        await service.ConnectAsync("http://localhost:5000", "student@skpp.sk", "pass");

        var (success, error, profile) = await service.VerifyAndRefreshProfileAsync();

        Assert.True(success);
        Assert.Null(error);
        Assert.True(profileEndpointCalled);
        Assert.NotNull(profile);
        Assert.Equal("Updated Student Name", profile.FullName);
        Assert.Equal(1660, profile.CurrentClassical);
    }

    [Fact]
    public async Task SkppService_GetAssignedHomeworksAsync_FetchesTasksWithBearerToken()
    {
        bool homeworksEndpointCalled = false;
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/api/auth/token"))
                {
                    var responsePayload = new SkppTokenResponse
                    {
                        AccessToken = "homework-bearer-token",
                        RefreshToken = "refresh-token-1",
                        ExpiresIn = 900,
                        User = new SkppUserDto { Role = "student", FullName = "Lukáš Študent" }
                    };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                    });
                }
                if (req.RequestUri.AbsolutePath.Contains("/api/caissalytics/homeworks"))
                {
                    homeworksEndpointCalled = true;
                    Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                    Assert.Equal("homework-bearer-token", req.Headers.Authorization?.Parameter);

                    var list = new List<SkppHomeworkDto>
                    {
                        new()
                        {
                            Id = 1004,
                            Title = "Taktická kombinácia: Vidlička jazdcom",
                            Description = "Nájdite najlepší ťah bieleho.",
                            Fen = "r1bqk2r/pp2bppp/2n1p3/2ppP3/3P4/2N2N2/PPP2PPP/R1BQKB1R w KQkq - 0 1",
                            CreatedByName = "Tomáš Kamenický",
                            MySubmission = new SkppSubmissionDto
                            {
                                Id = 1016,
                                Status = "Reviewed",
                                StudentAnswer = "1. dxc5 Bxc5 2. b4",
                                CoachFeedback = "Výborne vyriešené!",
                                Points = 10
                            }
                        },
                        new()
                        {
                            Id = 1003,
                            Title = "Taktická úloha: Mat 2. ťahom",
                            Description = "Biely na ťahu dá mat druhým ťahom.",
                            Fen = "r1bqkb1r/pppp1ppp/2n5/4p3/2B1n3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 4",
                            CreatedByName = "Tomáš Kamenický",
                            MySubmission = null
                        }
                    };

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(list), Encoding.UTF8, "application/json")
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        await service.ConnectAsync("http://localhost:5000", "student@skpp.sk", "Student123!");

        var homeworks = await service.GetAssignedHomeworksAsync();

        Assert.True(homeworksEndpointCalled);
        Assert.Equal(2, homeworks.Count);
        Assert.Equal("Reviewed", homeworks[0].StatusText);
        Assert.Equal(10, homeworks[0].MySubmission?.Points);
        Assert.Equal("Assigned", homeworks[1].StatusText);
        Assert.False(homeworks[1].HasSubmission);
    }

    [Fact]
    public async Task SkppService_SubmitHomeworkSolutionAsync_PostsSolutionAndReturnsSubmission()
    {
        bool submitEndpointCalled = false;
        string receivedBody = "";

        var handler = new MockHttpHandler
        {
            HandlerFunc = async req =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/api/auth/token"))
                {
                    var responsePayload = new SkppTokenResponse
                    {
                        AccessToken = "submit-token-xyz",
                        RefreshToken = "refresh-token-xyz",
                        ExpiresIn = 900,
                        User = new SkppUserDto { Role = "student", FullName = "Lukáš Študent" }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                    };
                }
                if (req.RequestUri.AbsolutePath.Contains("/api/caissalytics/homeworks/1003/submit"))
                {
                    submitEndpointCalled = true;
                    Assert.Equal(HttpMethod.Post, req.Method);
                    Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
                    Assert.Equal("submit-token-xyz", req.Headers.Authorization?.Parameter);

                    receivedBody = await req.Content!.ReadAsStringAsync();

                    var submissionResult = new SkppSubmissionDto
                    {
                        Id = 1018,
                        CoachingTaskId = 1003,
                        Status = "Submitted",
                        StudentAnswer = "1. Bxf7+ Kxf7 2. Ng5#",
                        SubmittedAt = DateTime.UtcNow
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(submissionResult), Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        await service.ConnectAsync("http://localhost:5000", "student@skpp.sk", "Student123!");

        var (success, error, submission) = await service.SubmitHomeworkSolutionAsync(
            1003,
            "1. Bxf7+ Kxf7 2. Ng5#",
            "1. Bxf7+ Kxf7 2. Ng5#");

        Assert.True(success);
        Assert.Null(error);
        Assert.True(submitEndpointCalled);
        Assert.NotNull(submission);
        Assert.Equal("Submitted", submission.Status);
        Assert.Equal(1003, submission.CoachingTaskId);

        // Verify request payload contained answer and pgn
        var reqObj = JsonSerializer.Deserialize<SkppSubmitHomeworkRequest>(receivedBody);
        Assert.NotNull(reqObj);
        Assert.Equal("1. Bxf7+ Kxf7 2. Ng5#", reqObj.Answer);
        Assert.Equal("1. Bxf7+ Kxf7 2. Ng5#", reqObj.Pgn);
    }

    [Theory]
    [InlineData("Lukáš Študent", "Lukáš", "Študent")]
    [InlineData("Tomáš Kamenický", "Tomáš", "Kamenický")]
    [InlineData("Jan Peter De Vries", "Jan", "Peter De Vries")]
    [InlineData("Kasparov", "Kasparov", "")]
    [InlineData("", "", "")]
    public void SkppUserDto_GetFirstAndLastName_ParsesCorrectly(string fullName, string expectedFirst, string expectedLast)
    {
        var dto = new SkppUserDto { FullName = fullName };
        var (first, last) = dto.GetFirstAndLastName();
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedLast, last);
    }

    [Fact]
    public async Task SkppService_ConnectAsync_UpdatesUserProfileFirstAndLastName()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                var responsePayload = new SkppTokenResponse
                {
                    AccessToken = "mock-jwt-profile-test",
                    RefreshToken = "mock-refresh-profile-test",
                    TokenType = "Bearer",
                    ExpiresIn = 900,
                    User = new SkppUserDto
                    {
                        Id = "u-student-42",
                        UserName = "lukas@skpp.sk",
                        Email = "lukas@skpp.sk",
                        FullName = "Lukáš Študent",
                        Role = "student"
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responsePayload), Encoding.UTF8, "application/json")
                });
            }
        };

        var profileFilePath = Path.Combine(_testDir, "user_profile_test.json");
        var userProfileService = new UserProfileService(profileFilePath);

        // Prepopulate with a default/anonymous profile
        await userProfileService.SaveProfileAsync(new UserProfile
        {
            FirstName = "OldFirst",
            LastName = "OldLast"
        });

        var httpClient = new HttpClient(handler);
        var skppService = new SkppIntegrationService(httpClient, _testDir, userProfileService);

        var (success, error) = await skppService.ConnectAsync("http://localhost:5000", "lukas@skpp.sk", "Student123!");

        Assert.True(success);
        Assert.Null(error);

        // Verify profile was updated with First Name and Last Name from SKPP
        var updatedProfile = await userProfileService.GetProfileAsync();
        Assert.Equal("Lukáš", updatedProfile.FirstName);
        Assert.Equal("Študent", updatedProfile.LastName);
        Assert.Equal("Lukáš Študent", updatedProfile.FullName);
    }

    [Fact]
    public async Task SkppService_GetMessagesAsync_ReturnsMessagesListAndComputesUnreadCount()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                Assert.Equal(HttpMethod.Get, req.Method);
                Assert.Contains("/api/caissalytics/messages", req.RequestUri!.ToString());
                Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
                Assert.Equal("mock-token-abc", req.Headers.Authorization.Parameter);

                var list = new List<SkppMessageDto>
                {
                    new()
                    {
                        Id = 1,
                        SenderName = "Tomáš Tréner",
                        Subject = "Rozbor partie",
                        Body = "Ahoj, pozri si ťahy.",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    },
                    new()
                    {
                        Id = 2,
                        SenderName = "Systém",
                        Subject = "Nová úloha",
                        Body = "Máš novú úlohu.",
                        IsRead = true,
                        CreatedAt = DateTime.UtcNow.AddHours(-1)
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(list), Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        // Pre-seed session
        string sessionFile = Path.Combine(_testDir, "skpp_session.json");
        var session = new SkppSessionData
        {
            ApiUrl = "http://localhost:5000",
            AccessToken = "mock-token-abc",
            RefreshToken = "mock-refresh-123",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CachedUser = new SkppUserDto { Role = "student" }
        };
        await File.WriteAllTextAsync(sessionFile, JsonSerializer.Serialize(session));

        int unreadEventVal = -1;
        service.OnUnreadCountChanged += count => unreadEventVal = count;

        var messages = await service.GetMessagesAsync();

        Assert.Equal(2, messages.Count);
        Assert.False(messages[0].IsRead);
        Assert.True(messages[1].IsRead);
        Assert.Equal(1, service.UnreadMessageCount);
        Assert.Equal(1, unreadEventVal);
    }

    [Fact]
    public async Task SkppService_MarkMessageAsReadAsync_SendsPostAndDecrementsUnreadCount()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = req =>
            {
                if (req.Method == HttpMethod.Get)
                {
                    var list = new List<SkppMessageDto>
                    {
                        new() { Id = 42, Subject = "Test", IsRead = false, CreatedAt = DateTime.UtcNow }
                    };
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(list), Encoding.UTF8, "application/json")
                    });
                }

                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("http://localhost:5000/api/caissalytics/messages/42/read", req.RequestUri!.ToString());

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        string sessionFile = Path.Combine(_testDir, "skpp_session.json");
        var session = new SkppSessionData
        {
            ApiUrl = "http://localhost:5000",
            AccessToken = "mock-token-abc",
            RefreshToken = "mock-refresh-123",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CachedUser = new SkppUserDto { Role = "student" }
        };
        await File.WriteAllTextAsync(sessionFile, JsonSerializer.Serialize(session));

        await service.GetMessagesAsync();
        Assert.Equal(1, service.UnreadMessageCount);

        bool marked = await service.MarkMessageAsReadAsync(42);
        Assert.True(marked);
        Assert.Equal(0, service.UnreadMessageCount);
    }

    [Fact]
    public async Task SkppService_SendMessageAsync_PostsCorrectPayload()
    {
        var handler = new MockHttpHandler
        {
            HandlerFunc = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("http://localhost:5000/api/caissalytics/messages", req.RequestUri!.ToString());

                string json = await req.Content!.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Assert.Equal("Otázka k otvoreniu", doc.RootElement.GetProperty("subject").GetString());
                Assert.Equal("Kedy hráme?", doc.RootElement.GetProperty("body").GetString());
                Assert.Equal(1, doc.RootElement.GetProperty("coachingGroupId").GetInt32());

                var created = new SkppMessageDto
                {
                    Id = 99,
                    Subject = "Otázka k otvoreniu",
                    Body = "Kedy hráme?",
                    CreatedAt = DateTime.UtcNow
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(created), Encoding.UTF8, "application/json")
                };
            }
        };

        var httpClient = new HttpClient(handler);
        var service = new SkppIntegrationService(httpClient, _testDir);

        string sessionFile = Path.Combine(_testDir, "skpp_session.json");
        var session = new SkppSessionData
        {
            ApiUrl = "http://localhost:5000",
            AccessToken = "mock-token-abc",
            RefreshToken = "mock-refresh-123",
            AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CachedUser = new SkppUserDto { Role = "student" }
        };
        await File.WriteAllTextAsync(sessionFile, JsonSerializer.Serialize(session));

        var (success, error, message) = await service.SendMessageAsync(new SkppSendMessageRequest
        {
            CoachingGroupId = 1,
            Subject = "Otázka k otvoreniu",
            Body = "Kedy hráme?"
        });

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(message);
        Assert.Equal(99, message.Id);
    }
}
