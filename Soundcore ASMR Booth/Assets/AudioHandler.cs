using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
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
    int maxRecordingTimeSeconds = 5; // The required recording duration
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



    //STEP 1
    // Data structures to match the JSON response bodies
    [System.Serializable]
    public class SignRequest
    {
        public string booth;
    }

    [System.Serializable]
    public class SignResponse
    {
        public string id;
        public string uploadUrl;
        public string rawKey;
        public string shareUrl;
        public int expiresIn;
        public string booth;
    }

    public IEnumerator GetSignedUrl(string boothId, string customId = "")
    {
        string url = "https://www.mysoundcore.com/api/upload/sign";

        // 1. Prepare Request Body
        SignRequest requestBody = new SignRequest { booth = boothId };
        string jsonBody = JsonUtility.ToJson(requestBody);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(jsonBytes);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Step 1 (Sign) Error: " + www.error);
            }
            else
            {
                // 2. Parse Response
                SignResponse response = JsonUtility.FromJson<SignResponse>(www.downloadHandler.text);
                Debug.Log($"Step 1 Success. Upload URL obtained: {response.uploadUrl}");

                // Proceed to Step 2 with the response data
                // StartCoroutine(UploadFileToSignedUrl(filePath, response.uploadUrl, response.id, response.booth));
            }
        }
    }


    //STEP 2

    public IEnumerator UploadFileToSignedUrl(string filePath, string uploadUrl, string fileId, string boothId)
    {
        // 1. Read the file into a byte array
        byte[] fileData = System.IO.File.ReadAllBytes(filePath);
        string mimeType = "audio/wav"; // Or determine based on file extension

        // Use UnityWebRequest.Put, which is designed for raw data uploads
        using (UnityWebRequest www = UnityWebRequest.Put(uploadUrl, fileData))
        {
            // NOTE: The signed URL may require specific headers, 
            // but Content-Type is the most common.
            www.SetRequestHeader("Content-Type", mimeType);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                // IMPORTANT: Upload failures here often mean an incorrect signed URL or missing/wrong header.
                Debug.LogError("Step 2 (Upload) Error: " + www.error);
            }
            else if (www.responseCode != 200)
            {
                // Cloud storage often returns a 200/204 on success, 
                // but sometimes a specific S3 error in the body for non-200 codes.
                Debug.LogError($"Step 2 (Upload) Failed with code {www.responseCode}. Response: {www.downloadHandler.text}");
            }
            else
            {
                Debug.Log("Step 2 Success. File uploaded to Cloud Storage.");
                // Proceed to Step 3
                // StartCoroutine(CommitUpload(fileId, boothId));
            }
        }
    }

    //STEP 3

    [System.Serializable]
    public class CommitRequest
    {
        public string id;
        public string booth;
    }

    [System.Serializable]
    public class CommitResponse
    {
        public string id;
        public string shareUrl;
        public string status; // e.g., "processing", "complete"
        public string booth;
    }

    public IEnumerator CommitUpload(string fileId, string boothId)
    {

        string url = "https://www.mysoundcore.com/api/upload/commit";

        // 1. Prepare Request Body
        CommitRequest requestBody = new CommitRequest { id = fileId, booth = boothId };
        string jsonBody = JsonUtility.ToJson(requestBody);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(jsonBytes);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Step 3 (Commit) Error: " + www.error);
            }
            else
            {
                // 2. Parse Response
                CommitResponse response = JsonUtility.FromJson<CommitResponse>(www.downloadHandler.text);
                Debug.Log($"Step 3 Success. File is {response.status}. Share URL: {response.shareUrl}");
                // Your upload is complete! The user can now use the shareUrl.
            }
        }
    }

    //combine=========================================

    public void StartFullUpload()
    {
        StartCoroutine(FullUploadSequence());
    }

    private IEnumerator FullUploadSequence()
    {

        string boothId = "B1";

        string signUrl = "https://www.mysoundcore.com/api/upload/sign";

        string persistentPath = Application.persistentDataPath;

        string[] wavFiles = Directory.GetFiles(persistentPath, "*.wav");

        if (wavFiles.Length == 0)
        {
            Debug.LogWarning($"No WAV files found in: {persistentPath}");
            yield break;
        }

        // 2. Find the most recently written file using LINQ
        string filePath = wavFiles
            // Convert each file path string into a FileInfo object to access LastWriteTime
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .First()
            .FullName;

        Debug.Log($"Found latest file: {filePath}");




        //SIGN UPLOAD
        SignRequest signBody = new SignRequest { booth = boothId};
        string jsonBody = JsonUtility.ToJson(signBody);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest signWww = new UnityWebRequest(signUrl, "POST");
        signWww.uploadHandler = new UploadHandlerRaw(jsonBytes);
        signWww.downloadHandler = new DownloadHandlerBuffer();
        signWww.SetRequestHeader("Content-Type", "application/json");

        yield return signWww.SendWebRequest();

        if (signWww.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Upload Failed: " + signWww.error);
            yield break;
        }

        SignResponse signResponse = JsonUtility.FromJson<SignResponse>(signWww.downloadHandler.text);
        string fileId = signResponse.id;
        string uploadUrl = signResponse.uploadUrl;
        Debug.Log($"Step 1: Signed URL received. ID: {fileId}");

        // --- 2. UPLOAD THE FILE ---
        byte[] fileData = System.IO.File.ReadAllBytes(filePath);

        using (UnityWebRequest uploadWww = UnityWebRequest.Put(uploadUrl, fileData))
        {
            uploadWww.SetRequestHeader("Content-Type", "audio/wav");

            yield return uploadWww.SendWebRequest();

            if (uploadWww.result != UnityWebRequest.Result.Success || uploadWww.responseCode >= 400)
            {
                Debug.LogError($"Upload Failed at Step 2 (PUT). Code: {uploadWww.responseCode}. Error: {uploadWww.error}");
                // Handle cleanup if necessary (e.g., notifying the server about a failed upload)
                yield break;
            }
            Debug.Log("Step 2: File uploaded successfully to R2/Storage.");
        }

        // --- 3. COMMIT THE UPLOAD ---
        string commitUrl = "https://www.mysoundcore.com/api/upload/commit";
        CommitRequest commitBody = new CommitRequest { id = fileId, booth = boothId };
        jsonBody = JsonUtility.ToJson(commitBody);
        jsonBytes = Encoding.UTF8.GetBytes(jsonBody);

        UnityWebRequest commitWww = new UnityWebRequest(commitUrl, "POST");
        commitWww.uploadHandler = new UploadHandlerRaw(jsonBytes);
        commitWww.downloadHandler = new DownloadHandlerBuffer();
        commitWww.SetRequestHeader("Content-Type", "application/json");

        yield return commitWww.SendWebRequest();

        if (commitWww.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Upload Failed at Step 3 (Commit): " + commitWww.error);
            yield break;
        }

        CommitResponse commitResponse = JsonUtility.FromJson<CommitResponse>(commitWww.downloadHandler.text);
        Debug.Log($"Upload Complete! Status: {commitResponse.status}. Final URL: {commitResponse.shareUrl}");
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