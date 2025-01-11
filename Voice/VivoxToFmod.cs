using UnityEngine;
using FMOD.Studio;
using FMODUnity;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FMOD;
using System;
using PLI.Logging;

public class VivoxToFmod : MonoBehaviour
{
    static private LogChannelShim Log => PLI.Logging.Core.Channels.Vivox;

    public EventReference eventName;

    private const int LatencyMS = 50;
    private const int DriftMS = 1;
    private const float DriftCorrectionPercentage = 0.5f;

    private int _systemSampleRate;
    private EventInstance _voiceEventInstance;
    private EVENT_CALLBACK _voiceCallback;

    private CREATESOUNDEXINFO _soundInfo;
    private Sound _voiceSound;
    private Channel _voiceChannel;

    private readonly List<float> _audioBuffer = new();
    private uint _bufferSamplesWritten;
    private uint _bufferReadPosition;
    private uint _driftThreshold;
    private uint _targetLatency;
    private uint _adjustedLatency;
    private int _actualLatency;
    private uint _totalSamplesWritten;
    private uint _totalSamplesRead;
    private uint _minimumSamplesWritten = uint.MaxValue;

    private bool _isSpeaking;

    private void Start()
    {
        // Log the system sample rate
        _systemSampleRate = AudioSettings.outputSampleRate;
        Log.Info($"System sample rate: {_systemSampleRate}");

        // Load and initialize FMOD voice event
        SetupFMOD();
    }

    private void SetupFMOD()
    {
        Log.Info("Setting up FMOD event for voice audio...");

        // Create event instance for "event:/Voice/Voice"
        _voiceEventInstance = RuntimeManager.CreateInstance("event:/Voice/Voice");

        if (_voiceEventInstance.isValid())
        {
            Log.Info("FMOD voice event instance created successfully.");
        }
        else
        {
            Log.Error("Failed to create FMOD voice event instance.");
        }

        // Assign the callback for programmer instruments
        _voiceCallback = new EVENT_CALLBACK(VoiceEventCallback);
        RESULT callbackResult = _voiceEventInstance.setCallback(_voiceCallback);
        if (callbackResult == RESULT.OK)
        {
            Log.Info("FMOD event callback set successfully.");
        }
        else
        {
            Log.Error($"Failed to set FMOD event callback: {callbackResult}");
        }

        // Start the event (this starts FMOD playback)
        _voiceEventInstance.start();
        _voiceEventInstance.setPaused(true); // Initially pause the event, so it doesn't play until there's audio
        Log.Info("FMOD voice event started and paused.");

        // Set up sound info for the FMOD sound that will handle Vivox audio
        _soundInfo.cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO));
        _soundInfo.numchannels = 1; // Assuming mono audio for voice chat
        _soundInfo.defaultfrequency = _systemSampleRate;
        _soundInfo.length = (uint)(_systemSampleRate * sizeof(float) * LatencyMS) / 1000;
        _soundInfo.format = SOUND_FORMAT.PCMFLOAT;

        Log.Info("FMOD sound info setup completed.");
    }

    // FMOD callback to handle programmer instruments
    [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
    private static RESULT VoiceEventCallback(EVENT_CALLBACK_TYPE type, IntPtr instancePtr, IntPtr parameterPtr)
    {
        var instance = new EventInstance(instancePtr);
        instance.getUserData(out IntPtr soundPtr);

        if (soundPtr == IntPtr.Zero) return RESULT.OK;

        var soundHandle = GCHandle.FromIntPtr(soundPtr);
        var sound = (Sound)soundHandle.Target;

        switch (type)
        {
            case EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND:
                {
                    Log.Info("Creating programmer sound for FMOD event...");

                    if (soundPtr != IntPtr.Zero)
                    {
                        var parameter = (PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(parameterPtr,
                            typeof(PROGRAMMER_SOUND_PROPERTIES));
                        parameter.sound = sound.handle;
                        parameter.subsoundIndex = -1;
                        Marshal.StructureToPtr(parameter, parameterPtr, false);
                        Log.Info("Programmer sound created and linked to FMOD event.");
                        break;
                    }
                    else
                    {
                        Log.Warn("Received a null sound pointer while creating programmer sound.");
                        break;
                    }
                }
            case EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND:
                {
                    var parameter = (PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(parameterPtr,
                        typeof(PROGRAMMER_SOUND_PROPERTIES));
                    sound.release();
                    sound = new(parameter.sound);
                    sound.release();
                    break;
                }
            case EVENT_CALLBACK_TYPE.DESTROYED:
                {
                    soundHandle.Free();
                    break;
                }
        }

        return RESULT.OK;
    }

    private void UpdateBufferLatency(uint samplesWritten)
    {
        _totalSamplesWritten += samplesWritten;

        if (samplesWritten != 0 && samplesWritten < _minimumSamplesWritten)
        {
            _minimumSamplesWritten = samplesWritten;
            _adjustedLatency = Math.Max(samplesWritten, _targetLatency);
        }

        int latency = (int)_totalSamplesWritten - (int)_totalSamplesRead;
        _actualLatency = (int)(0.93f * _actualLatency + 0.03f * latency);

        if (!_voiceChannel.hasHandle()) return;

        int playbackRate = _systemSampleRate;
        if (_actualLatency < (int)(_adjustedLatency - _driftThreshold))
        {
            playbackRate = _systemSampleRate - (int)(_systemSampleRate * (DriftCorrectionPercentage / 100.0f));
        }
        else if (_actualLatency > (int)(_adjustedLatency + _driftThreshold))
        {
            playbackRate = _systemSampleRate + (int)(_systemSampleRate * (DriftCorrectionPercentage / 100.0f));
        }

        _voiceChannel.setFrequency(playbackRate);
    }

    // Unity's OnAudioFilterRead method to capture audio data from Vivox
    private void OnAudioFilterRead(float[] data, int channels)
    {
        // Add the incoming audio data to the buffer
        if (_voiceChannel.hasHandle())
        {
            _audioBuffer.AddRange(data);
            UpdateBufferLatency((uint)data.Length);
        }

        // Check if the participant is speaking (non-zero audio data)
        _isSpeaking = false;
        foreach (float sample in data)
        {
            if (sample != 0)
            {
                _isSpeaking = true;
                break;
            }
        }

        if (_isSpeaking)
        {
            //Log.Info("Participant is speaking, processing audio buffer...");
            // Process the audio buffer and play it via FMOD
            ProcessAudio(channels);
        }

        // Clear the audio buffer after processing
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = 0;
        }
    }

    private void ProcessAudio(int channels)
    {
        if (!_voiceChannel.hasHandle())
        {
            if (!_isSpeaking) return;

            //Log.Info($"Sound info cbsize = {_soundInfo.cbsize}");
            // Create FMOD sound from Vivox audio data
            RESULT result = RuntimeManager.CoreSystem.createSound("vivox", MODE.LOOP_NORMAL | MODE.OPENUSER, ref _soundInfo, out _voiceSound);

            if (result == RESULT.OK)
            {
                Log.Info("FMOD sound created successfully.");
            }
            else
            {
                Log.Error($"Failed to create FMOD sound: {result}");
                return;
            }

            // Play sound on a new channel
            _voiceEventInstance.getChannelGroup(out ChannelGroup channelGroup);
            RuntimeManager.CoreSystem.playSound(_voiceSound, channelGroup, false, out _voiceChannel);

            if (_voiceChannel.hasHandle())
            {
                Log.Info("FMOD sound playing on the new channel.");
            }
            else
            {
                Log.Error("Failed to play sound on FMOD channel.");
            }
        }
            Log.Info($"Processing {_audioBuffer.Count} audio samples.");

        // Send audio from buffer to FMOD
        if (_audioBuffer.Count == 0) return;

        _voiceChannel.getPosition(out uint readPosition, TIMEUNIT.PCMBYTES);

        uint bytesRead = readPosition - _bufferReadPosition;
        if (readPosition <= _bufferReadPosition)
        {
            bytesRead += _soundInfo.length;
        }

        if (bytesRead <= 0 || _audioBuffer.Count < bytesRead) return;

        RESULT res = _voiceSound.@lock(_bufferReadPosition, bytesRead, out IntPtr ptr1, out IntPtr ptr2, out uint len1,
            out uint len2);
        if (res != RESULT.OK)
        {
            Log.Error(res.ToString());
        }

        // Though soundInfo.format is float, data retrieved from Sound::lock is in bytes,
        // so we only copy (len1+len2)/sizeof(float) full float values across
        int sampleLen1 = (int)(len1 / sizeof(float));
        int sampleLen2 = (int)(len2 / sizeof(float));
        int samplesRead = sampleLen1 + sampleLen2;
        float[] tmpBuffer = new float[samplesRead];

        _audioBuffer.CopyTo(0, tmpBuffer, 0, tmpBuffer.Length);
        _audioBuffer.RemoveRange(0, tmpBuffer.Length);

        if (len1 > 0)
        {
            Marshal.Copy(tmpBuffer, 0, ptr1, sampleLen1);
        }
        if (len2 > 0)
        {
            Marshal.Copy(tmpBuffer, sampleLen1, ptr2, sampleLen2);
        }

        res = _voiceSound.unlock(ptr1, ptr2, len1, len2);
        if (res != RESULT.OK)
        {
            Log.Error(res.ToString());
        }

        _bufferReadPosition = readPosition;
        _totalSamplesRead += (uint)samplesRead;

        var soundHandle = GCHandle.Alloc(_voiceSound, GCHandleType.Pinned);
        _voiceEventInstance.setUserData(GCHandle.ToIntPtr(soundHandle));
    }

    private void OnDestroy()
    {
        Log.Info("OnDestroy called, releasing FMOD resources.");

        // Clean up FMOD resources
        _voiceSound.release();
        Log.Info("FMOD sound released.");

        _voiceEventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        Log.Info("FMOD voice event stopped.");

        _voiceEventInstance.release();
        Log.Info("FMOD voice event instance released.");
    }
}
