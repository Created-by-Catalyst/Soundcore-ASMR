using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class AudioHandler : MonoBehaviour
{
    public GlobalManager globalManager;
    public TMP_Text timerText;

    // --- Public Configuration ---
    public TMP_Text buttonText; // Link this to your button's text component

    [Header("Recording Settings")]
    public int maxRecordingTimeSeconds = 25; // The required recording duration
    public int sampleRate = 44100; // Standard audio sample rate

    // --- Private Fields ---
    private AudioClip recordedClip;
    private string microphoneName;
    private bool isRecording = false;


    void Start()
    {

        // 1. Get the primary microphone
        if (Microphone.devices.Length > 0)
        {
            microphoneName = Microphone.devices.FirstOrDefault();
            Debug.Log($"Using microphone: {microphoneName}");
            if (buttonText != null) buttonText.text = "START RECORDING (25s)";
        }
        else
        {
            Debug.LogError("No microphone devices found!");
            if (buttonText != null) buttonText.text = "NO MIC FOUND";
        }
    }

    // --- Public Method to be called by the UI Button ---
    public void StartRecording()
    {
        if (isRecording || string.IsNullOrEmpty(microphoneName)) return;

        // Start the microphone recording
        // We set 'loop' to false and use the configured max duration.
        recordedClip = Microphone.Start(microphoneName, false, maxRecordingTimeSeconds, sampleRate);
        isRecording = true;

        Debug.Log($"Recording started for {maxRecordingTimeSeconds} seconds...");
        if (buttonText != null) buttonText.text = "RECORDING...";

        // Start the coroutine to automatically stop and save the audio
        StartCoroutine(StopRecordingAndSaveAfterDelay(maxRecordingTimeSeconds));
    }

    private IEnumerator StopRecordingAndSaveAfterDelay(float delay)
    {

        float recordingProgress = delay;

        while (recordingProgress > 0)
        {
            recordingProgress -= Time.deltaTime;
            timerText.text = $"00:{recordingProgress:00}";
            yield return null;
        }

        timerText.text = "00:00";


        // Only proceed if recording is still active (user didn't press stop early)
        if (isRecording)
        {
            StopRecordingAndSave();
        }
    }

    private void StopRecordingAndSave()
    {
        if (!isRecording) return;

        // 2. Stop the microphone input
        Microphone.End(microphoneName);
        isRecording = false;

        Debug.Log("Recording stopped.");

        // 3. Trim the clip to the actual recorded length (important!)
        recordedClip = TrimClip(recordedClip, Microphone.GetPosition(microphoneName));

        // 4. Determine the file path
        string filename = $"{DateTime.Now:yyyyMMddHHmmss}.wav";
        string path = Path.Combine(Application.persistentDataPath, filename);

        // 5. Save the AudioClip to a WAV file
        if (WaveFileWriter.SaveWav(path, recordedClip))
        {
            Debug.Log($"Audio saved successfully to: {path}");
            if (buttonText != null)
            {
                buttonText.text = $"SAVED! ({filename})";
            }
            // Optional: Play back the saved clip for confirmation
            //audioSource.clip = recordedClip;
            //audioSource.Play();
        }
        else
        {
            Debug.LogError("Failed to save audio file.");
            if (buttonText != null) buttonText.text = "SAVE FAILED";
        }


        globalManager.NextPage();
    }

    // --- Utility: Trims the AudioClip to the actual number of recorded samples ---
    private AudioClip TrimClip(AudioClip clip, int endPosition)
    {
        if (endPosition <= 0 || endPosition >= clip.samples)
        {
            return clip; // No trimming needed
        }

        // Get recorded data
        float[] data = new float[endPosition * clip.channels];
        clip.GetData(data, 0);

        // Create a new clip with the correct length
        AudioClip newClip = AudioClip.Create(clip.name, endPosition, clip.channels, clip.frequency, false);
        newClip.SetData(data, 0);

        Destroy(clip); // Clean up the old, longer clip
        return newClip;
    }












    //UPLOAD------------------------------------------------------------------------------------------



    private const string url = "https://www.mysoundcore.com/api/upload";

    private const string WavFileName = "20251029111143.wav";

    public void StartUpload()
    {
        StartCoroutine(UploadWavFile());
    }

    private IEnumerator UploadWavFile()
    {
        string persistentPath = Application.persistentDataPath;

        string[] wavFiles = Directory.GetFiles(persistentPath, "*.wav");

        if (wavFiles.Length == 0)
        {
            Debug.LogWarning($"No WAV files found in: {persistentPath}");
            yield break;
        }

        // 2. Find the most recently written file using LINQ
        string latestFilePath = wavFiles
            // Convert each file path string into a FileInfo object to access LastWriteTime
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .First()
            .FullName;

        Debug.Log($"Found latest file: {latestFilePath}");


        byte[] wavBytes = File.ReadAllBytes(latestFilePath);

        IMultipartFormSection fileSection = new MultipartFormFileSection(
            "file",
            wavBytes,
            WavFileName,
            "audio/wav"
        );

        var formData = new System.Collections.Generic.List<IMultipartFormSection> {
            fileSection
        };

        //POST
        using (UnityWebRequest www = UnityWebRequest.Post(url, formData))
        {
            Debug.Log($"Starting upload to: {url}");


            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Upload failed: {www.error}");
                Debug.LogError($"Response code: {www.responseCode}");
            }
            else
            {
                Debug.Log("WAV file uploaded successfully!");
                Debug.Log($"Server Response: {www.downloadHandler.text}");
            }
        }
    }


}

public static class WaveFileWriter
{
    // Saves the AudioClip as a standard WAV file
    public static bool SaveWav(string filepath, AudioClip clip)
    {
        try
        {
            using (var fileStream = new FileStream(filepath, FileMode.Create))
            {
                // Write WAV header and data
                return WriteWavFile(fileStream, clip);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving WAV file: {ex.Message}");
            return false;
        }
    }

    // A simplified method to write all the necessary WAV header information and audio data
    public static bool WriteWavFile(FileStream fileStream, AudioClip clip)
    {
        if (clip == null) return false;

        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        byte[] bytes = ConvertTo16Bit(samples);

        int hz = clip.frequency;
        int channels = clip.channels;

        // Calculate file sizes for the header
        int riffChunkSize = 36 + bytes.Length;
        int waveDataSize = bytes.Length;
        int averageBytesPerSecond = hz * channels * 2;

        // Write the main RIFF header
        WriteBytes(fileStream, "RIFF");
        WriteInt(fileStream, riffChunkSize);
        WriteBytes(fileStream, "WAVE");

        // Write the 'fmt ' sub-chunk
        WriteBytes(fileStream, "fmt ");
        WriteInt(fileStream, 16);
        WriteShort(fileStream, 1); // PCM format
        WriteShort(fileStream, (short)channels);
        WriteInt(fileStream, hz);
        WriteInt(fileStream, averageBytesPerSecond);
        WriteShort(fileStream, (short)(channels * 2));
        WriteShort(fileStream, 16); // 16 Bits Per Sample

        // Write the 'data' sub-chunk
        WriteBytes(fileStream, "data");
        WriteInt(fileStream, waveDataSize);

        // Write the actual audio data
        fileStream.Write(bytes, 0, bytes.Length);

        return true;
    }

    private static byte[] ConvertTo16Bit(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            // Convert float (-1 to 1) to 16-bit integer (-32768 to 32767)
            short sample = (short)(samples[i] * 32767);

            // Write the short as two bytes (Little Endian format for WAV)
            bytes[i * 2] = (byte)(sample & 0xFF);
            bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return bytes;
    }

    private static void WriteBytes(FileStream stream, string id)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(id);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteInt(FileStream stream, int value)
    {
        stream.Write(BitConverter.GetBytes(value), 0, 4);
    }

    private static void WriteShort(FileStream stream, short value)
    {
        stream.Write(BitConverter.GetBytes(value), 0, 2);
    }


    


}