using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class STTRequest
{
    public string audio_base64;
    public int sample_rate = 16000;
    public string format = "wav";
}

[Serializable]
public class STTResponse
{
    public string transcription;
    public bool success;
    public string error;
}

[Serializable]
public class TTSRequest
{
    public string text;
    public string voice = "korean";
}

[Serializable]
public class TTSResponse
{
    public string audio_base64;
    public bool success;
    public string error;
}

public class SpeechService : MonoBehaviour
{
    [SerializeField] private Parameters parameters;
    
    private static SpeechService instance;
    public static SpeechService Instance => instance;
    
    void Awake()
    {
        instance = this;
    }
    
    public async Task<string> RecognizeSpeech(byte[] audioData)
    {
        string url = parameters.serverUrl + "/api/stt";
        
        var request = new STTRequest
        {
            audio_base64 = Convert.ToBase64String(audioData),
            sample_rate = parameters.sampleRate,
            format = "wav"
        };
        
        string json = JsonUtility.ToJson(request);
        
        using (UnityWebRequest www = UnityWebRequest.Post(url, json, "application/json"))
        {
            www.timeout = (int)parameters.requestTimeout;
            
            var operation = www.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Delay(10);
            }
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<STTResponse>(www.downloadHandler.text);
                if (response.success)
                {
                    return response.transcription;
                }
                else
                {
                    Debug.LogError($"[Speech] STT错误: {response.error}");
                    return null;
                }
            }
            else
            {
                Debug.LogError($"[Speech] 网络错误: {www.error}");
                return null;
            }
        }
    }
    
    public async Task<AudioClip> SynthesizeSpeech(string text)
    {
        string url = parameters.serverUrl + "/api/tts";
        
        var request = new TTSRequest
        {
            text = text,
            voice = "korean"
        };
        
        string json = JsonUtility.ToJson(request);
        
        using (UnityWebRequest www = UnityWebRequest.Post(url, json, "application/json"))
        {
            www.timeout = (int)parameters.requestTimeout;
            
            var operation = www.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Delay(10);
            }
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<TTSResponse>(www.downloadHandler.text);
                if (response.success)
                {
                    byte[] audioBytes = Convert.FromBase64String(response.audio_base64);
                    
                    // 将WAV字节转换为AudioClip
                    float[] samples = ConvertByteToFloat(audioBytes);
                    AudioClip clip = AudioClip.Create("TTS", samples.Length, 1, parameters.sampleRate, false);
                    clip.SetData(samples, 0);
                    
                    return clip;
                }
                else
                {
                    Debug.LogError($"[Speech] TTS错误: {response.error}");
                    return null;
                }
            }
            else
            {
                Debug.LogError($"[Speech] 网络错误: {www.error}");
                return null;
            }
        }
    }
    
    private float[] ConvertByteToFloat(byte[] wavFile)
    {
        // 跳过WAV头部(44字节)
        int headerSize = 44;
        int dataSize = wavFile.Length - headerSize;
        float[] samples = new float[dataSize / 2];
        
        int sampleIndex = 0;
        for (int i = headerSize; i < wavFile.Length - 1; i += 2)
        {
            short sample = (short)((wavFile[i + 1] << 8) | wavFile[i]);
            samples[sampleIndex++] = sample / 32768f;
        }
        
        return samples;
    }
}