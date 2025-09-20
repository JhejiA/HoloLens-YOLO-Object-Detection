using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Windows.Speech;
using Microsoft.MixedReality.Toolkit.Audio;

public class VoiceController : MonoBehaviour
{
    [SerializeField] private Parameters parameters;
    [SerializeField] private AudioSource audioSource;
    
    private KeywordRecognizer keywordRecognizer;
    private ApplicationFSM fsm;
    private SpeechService speechService;
    
    private bool isRecording = false;
    private AudioClip recordingClip;
    
    void Start()
    {
        fsm = GetComponent<ApplicationFSM>();
        speechService = GetComponent<SpeechService>();
        
        SetupKeywordRecognizer();
    }
    
    private void SetupKeywordRecognizer()
    {
        // 设置唤醒词识别
        string[] keywords = new string[] { parameters.wakeWord };
        keywordRecognizer = new KeywordRecognizer(keywords);
        keywordRecognizer.OnPhraseRecognized += OnWakeWordDetected;
        keywordRecognizer.Start();
        
        Debug.Log($"[Voice] 唤醒词识别已启动: {parameters.wakeWord}");
    }
    
    private void OnWakeWordDetected(PhraseRecognizedEventArgs args)
    {
        Debug.Log($"[Voice] 检测到唤醒词: {args.text}");
        
        if (fsm.CurrentState == AppState.IDLE)
        {
            StartListening();
        }
    }
    
    private async void StartListening()
    {
        fsm.TransitionTo(AppState.LISTENING);
        
        // 开始录音
        isRecording = true;
        recordingClip = Microphone.Start(null, false, (int)parameters.recordingLength, parameters.sampleRate);
        
        // 等待录音完成
        await Task.Delay((int)(parameters.recordingLength * 1000));
        
        if (isRecording)
        {
            StopListening();
        }
    }
    
    private async void StopListening()
    {
        isRecording = false;
        Microphone.End(null);
        
        fsm.TransitionTo(AppState.PROCESSING_SPEECH);
        
        // 将录音转换为WAV字节
        byte[] audioData = ConvertAudioClipToWAV(recordingClip);
        
        // 发送到服务器进行语音识别
        string transcription = await speechService.RecognizeSpeech(audioData);
        
        if (!string.IsNullOrEmpty(transcription))
        {
            ProcessTranscription(transcription);
        }
        else
        {
            // 识别失败
            fsm.TransitionTo(AppState.ERROR);
        }
    }
    
    private async void ProcessTranscription(string text)
    {
        Debug.Log($"[Voice] 识别结果: {text}");
        
        // 提取商品名称
        string targetProduct = ExtractProductName(text);
        
        if (!string.IsNullOrEmpty(targetProduct))
        {
            MainController.Instance.CurrentSearchTarget = targetProduct;
            
            // 播放确认音
            string confirmText = $"{targetProduct}을 찾고 있습니다";
            AudioClip confirmAudio = await speechService.SynthesizeSpeech(confirmText);
            
            if (confirmAudio != null)
            {
                audioSource.clip = confirmAudio;
                audioSource.Play();
            }
            
            // 开始搜索
            fsm.TransitionTo(AppState.SEARCHING);
        }
        else
        {
            // 未识别到商品名
            string errorText = "상품명을 인식하지 못했습니다. 다시 말씀해 주세요.";
            AudioClip errorAudio = await speechService.SynthesizeSpeech(errorText);
            
            if (errorAudio != null)
            {
                audioSource.clip = errorAudio;
                audioSource.Play();
            }
            
            fsm.TransitionTo(AppState.IDLE);
        }
    }
    
    private string ExtractProductName(string text)
    {
        // 检查文本中是否包含商品名
        foreach (string product in parameters.productNames)
        {
            if (text.Contains(product))
            {
                return product;
            }
        }
        return null;
    }
    
    private byte[] ConvertAudioClipToWAV(AudioClip clip)
    {
        int samples = clip.samples * clip.channels;
        float[] data = new float[samples];
        clip.GetData(data, 0);
        
        byte[] wav = new byte[44 + samples * 2];
        
        // WAV头部
        System.Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes("RIFF"), 0, wav, 0, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(wav.Length - 8), 0, wav, 4, 4);
        System.Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes("WAVE"), 0, wav, 8, 4);
        System.Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes("fmt "), 0, wav, 12, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(16), 0, wav, 16, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, wav, 20, 2);
        System.Buffer.BlockCopy(BitConverter.GetBytes((short)clip.channels), 0, wav, 22, 2);
        System.Buffer.BlockCopy(BitConverter.GetBytes(clip.frequency), 0, wav, 24, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(clip.frequency * clip.channels * 2), 0, wav, 28, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes((short)(clip.channels * 2)), 0, wav, 32, 2);
        System.Buffer.BlockCopy(BitConverter.GetBytes((short)16), 0, wav, 34, 2);
        System.Buffer.BlockCopy(System.Text.Encoding.UTF8.GetBytes("data"), 0, wav, 36, 4);
        System.Buffer.BlockCopy(BitConverter.GetBytes(samples * 2), 0, wav, 40, 4);
        
        // 音频数据
        int offset = 44;
        for (int i = 0; i < data.Length; i++)
        {
            short sample = (short)(data[i] * 32767);
            System.Buffer.BlockCopy(BitConverter.GetBytes(sample), 0, wav, offset, 2);
            offset += 2;
        }
        
        return wav;
    }
    
    void OnDestroy()
    {
        keywordRecognizer?.Stop();
        keywordRecognizer?.Dispose();
    }
}

// ===============================================
// 7. MainController.cs - 主控制器 [Assets/Scripts/MainController.cs]
// ===============================================
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MainController : MonoBehaviour
{
    [Header("组件引用")]
    [SerializeField] private ApplicationFSM fsm;
    [SerializeField] private YoloDetector yoloDetector;
    [SerializeField] private VoiceController voiceController;
    [SerializeField] private SpeechService speechService;
    [SerializeField] private ObjectPoolManager poolManager;
    
    [Header("UI元素")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject processingIndicator;
    [SerializeField] private Button resetButton;
    
    public static MainController Instance { get; private set; }
    public string CurrentSearchTarget { get; set; }
    
    void Awake()
    {
        Instance = this;
        
        // 确保所有组件存在
        if (fsm == null) fsm = GetComponent<ApplicationFSM>();
        if (yoloDetector == null) yoloDetector = GetComponent<YoloDetector>();
        if (voiceController == null) voiceController = GetComponent<VoiceController>();
        if (speechService == null) speechService = GetComponent<SpeechService>();
        if (poolManager == null) poolManager = FindObjectOfType<ObjectPoolManager>();
    }
    
    void Start()
    {
        // 订阅状态变化事件
        ApplicationFSM.OnStateChanged += OnStateChanged;
        
        // 设置重置按钮
        if (resetButton != null)
        {
            resetButton.onClick.AddListener(ResetSystem);
        }
        
        // 初始状态
        UpdateUI(AppState.IDLE);
    }
    
    private void OnStateChanged(AppState oldState, AppState newState)
    {
        UpdateUI(newState);
    }
    
    private void UpdateUI(AppState state)
    {
        // 更新状态文本
        if (statusText != null)
        {
            statusText.text = GetStatusText(state);
        }
        
        // 更新处理指示器
        if (processingIndicator != null)
        {
            processingIndicator.SetActive(
                state == AppState.PROCESSING_SPEECH || 
                state == AppState.SEARCHING
            );
        }
    }
    
    private string GetStatusText(AppState state)
    {
        return state switch
        {
            AppState.IDLE => $"준비됨 - '{voiceController?.parameters?.wakeWord}'라고 말씀하세요",
            AppState.LISTENING => "듣고 있습니다...",
            AppState.PROCESSING_SPEECH => "음성 처리 중...",
            AppState.SEARCHING => $"'{CurrentSearchTarget}' 찾는 중...",
            AppState.FOUND => $"'{CurrentSearchTarget}' 찾았습니다!",
            AppState.ERROR => "오류 발생 - 다시 시도하세요",
            _ => "알 수 없는 상태"
        };
    }
    
    private void ResetSystem()
    {
        // 清空所有检测结果
        poolManager?.ClearAll();
        
        // 重置搜索目标
        CurrentSearchTarget = null;
        
        // 返回空闲状态
        fsm?.TransitionTo(AppState.IDLE);
    }
    
    void OnDestroy()
    {
        ApplicationFSM.OnStateChanged -= OnStateChanged;
    }
}