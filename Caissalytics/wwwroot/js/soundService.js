// Caissalytics Web Audio API Synthesizer
// Provides zero-latency, realistic synthesized sound effects without external audio files.

window.chessSound = (function () {
    let audioCtx = null;

    function getAudioContext() {
        if (!audioCtx) {
            const AudioContextClass = window.AudioContext || window.webkitAudioContext;
            if (AudioContextClass) {
                audioCtx = new AudioContextClass();
            }
        }
        if (audioCtx && audioCtx.state === 'suspended') {
            audioCtx.resume();
        }
        return audioCtx;
    }

    function createMasterGain(ctx, volume) {
        const gain = ctx.createGain();
        const clampedVol = Math.max(0, Math.min(1, typeof volume === 'number' ? volume : 0.8));
        gain.gain.setValueAtTime(clampedVol, ctx.currentTime);
        gain.connect(ctx.destination);
        return gain;
    }

    return {
        playMove: function (volume) {
            try {
                const ctx = getAudioContext();
                if (!ctx) return;
                const master = createMasterGain(ctx, volume);
                const now = ctx.currentTime;

                // 1. Resonant wood thud
                const osc = ctx.createOscillator();
                const oscGain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(180, now);
                osc.frequency.exponentialRampToValueAtTime(65, now + 0.08);

                oscGain.gain.setValueAtTime(0.7, now);
                oscGain.gain.exponentialRampToValueAtTime(0.001, now + 0.09);

                osc.connect(oscGain);
                oscGain.connect(master);

                // 2. Crisp surface contact click
                const bufferSize = ctx.sampleRate * 0.02; // 20ms noise
                const buffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate);
                const data = buffer.getChannelData(0);
                for (let i = 0; i < bufferSize; i++) {
                    data[i] = Math.random() * 2 - 1;
                }

                const noise = ctx.createBufferSource();
                noise.buffer = buffer;

                const filter = ctx.createBiquadFilter();
                filter.type = 'bandpass';
                filter.frequency.setValueAtTime(1200, now);
                filter.Q.setValueAtTime(3, now);

                const noiseGain = ctx.createGain();
                noiseGain.gain.setValueAtTime(0.35, now);
                noiseGain.gain.exponentialRampToValueAtTime(0.001, now + 0.025);

                noise.connect(filter);
                filter.connect(noiseGain);
                noiseGain.connect(master);

                osc.start(now);
                osc.stop(now + 0.1);
                noise.start(now);
                noise.stop(now + 0.03);
            } catch (e) {
                console.warn("ChessSound move error:", e);
            }
        },

        playCapture: function (volume) {
            try {
                const ctx = getAudioContext();
                if (!ctx) return;
                const master = createMasterGain(ctx, volume);
                const now = ctx.currentTime;

                // 1. Heavier impact body
                const osc = ctx.createOscillator();
                const oscGain = ctx.createGain();
                osc.type = 'triangle';
                osc.frequency.setValueAtTime(260, now);
                osc.frequency.exponentialRampToValueAtTime(50, now + 0.12);

                oscGain.gain.setValueAtTime(0.9, now);
                oscGain.gain.exponentialRampToValueAtTime(0.001, now + 0.13);

                osc.connect(oscGain);
                oscGain.connect(master);

                // 2. High snap click
                const snapOsc = ctx.createOscillator();
                const snapGain = ctx.createGain();
                snapOsc.type = 'sine';
                snapOsc.frequency.setValueAtTime(800, now);
                snapOsc.frequency.exponentialRampToValueAtTime(150, now + 0.04);

                snapGain.gain.setValueAtTime(0.5, now);
                snapGain.gain.exponentialRampToValueAtTime(0.001, now + 0.045);

                snapOsc.connect(snapGain);
                snapGain.connect(master);

                // 3. Tactile noise crack
                const bufferSize = ctx.sampleRate * 0.035;
                const buffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate);
                const data = buffer.getChannelData(0);
                for (let i = 0; i < bufferSize; i++) {
                    data[i] = Math.random() * 2 - 1;
                }

                const noise = ctx.createBufferSource();
                noise.buffer = buffer;

                const filter = ctx.createBiquadFilter();
                filter.type = 'bandpass';
                filter.frequency.setValueAtTime(2200, now);
                filter.Q.setValueAtTime(2, now);

                const noiseGain = ctx.createGain();
                noiseGain.gain.setValueAtTime(0.4, now);
                noiseGain.gain.exponentialRampToValueAtTime(0.001, now + 0.035);

                noise.connect(filter);
                filter.connect(noiseGain);
                noiseGain.connect(master);

                osc.start(now);
                osc.stop(now + 0.14);
                snapOsc.start(now);
                snapOsc.stop(now + 0.05);
                noise.start(now);
                noise.stop(now + 0.04);
            } catch (e) {
                console.warn("ChessSound capture error:", e);
            }
        },

        playCheck: function (volume) {
            try {
                const ctx = getAudioContext();
                if (!ctx) return;
                const master = createMasterGain(ctx, volume);
                const now = ctx.currentTime;

                // Dual harmonic bell / alert chime (F#5 + C#6)
                const freqs = [739.99, 1108.73];
                freqs.forEach((freq, idx) => {
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();

                    osc.type = 'sine';
                    osc.frequency.setValueAtTime(freq, now);

                    gain.gain.setValueAtTime(0.35, now);
                    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.28 + idx * 0.05);

                    osc.connect(gain);
                    gain.connect(master);

                    osc.start(now);
                    osc.stop(now + 0.35);
                });
            } catch (e) {
                console.warn("ChessSound check error:", e);
            }
        },

        playVictory: function (volume) {
            try {
                const ctx = getAudioContext();
                if (!ctx) return;
                const master = createMasterGain(ctx, volume);
                const now = ctx.currentTime;

                // Sparkling ascending major triad: C5, E5, G5, C6
                const notes = [523.25, 659.25, 783.99, 1046.50];
                notes.forEach((freq, idx) => {
                    const startTime = now + (idx * 0.08);
                    const osc = ctx.createOscillator();
                    const gain = ctx.createGain();

                    osc.type = 'triangle';
                    osc.frequency.setValueAtTime(freq, startTime);

                    gain.gain.setValueAtTime(0.001, startTime);
                    gain.gain.linearRampToValueAtTime(0.35, startTime + 0.02);
                    gain.gain.exponentialRampToValueAtTime(0.001, startTime + 0.4);

                    osc.connect(gain);
                    gain.connect(master);

                    osc.start(startTime);
                    osc.stop(startTime + 0.45);
                });
            } catch (e) {
                console.warn("ChessSound victory error:", e);
            }
        },

        playLowTime: function (volume) {
            try {
                const ctx = getAudioContext();
                if (!ctx) return;
                const master = createMasterGain(ctx, volume);
                const now = ctx.currentTime;

                // Clock tick
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();

                osc.type = 'sine';
                osc.frequency.setValueAtTime(1400, now);
                osc.frequency.exponentialRampToValueAtTime(400, now + 0.03);

                gain.gain.setValueAtTime(0.3, now);
                gain.gain.exponentialRampToValueAtTime(0.001, now + 0.035);

                osc.connect(gain);
                gain.connect(master);

                osc.start(now);
                osc.stop(now + 0.04);
            } catch (e) {
                console.warn("ChessSound lowTime error:", e);
            }
        }
    };
})();
