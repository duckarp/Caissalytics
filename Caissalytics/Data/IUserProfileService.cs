namespace Caissalytics.Data;

public interface IUserProfileService
{
    Task<UserProfile> GetProfileAsync();
    Task SaveProfileAsync(UserProfile profile);
    event Action? OnProfileChanged;
}
