using UnityEngine;

[CreateAssetMenu(fileName = "YOLOParameters", menuName = "YOLO/Parameters")]
public class Parameters : ScriptableObject
{
    [Header("模型配置")]
    public string modelPath = "yolov11n_fp16.onnx";
    public string int8ModelPath = "yolov11n_int8.onnx";
    public bool useInt8Model = false;
    public int imageSize = 512;
    
    [Header("检测参数")]
    public float confidenceThreshold = 0.45f;
    public float nmsThreshold = 0.5f;
    public int frameSkipInterval = 2; // 每2帧检测1次
    
    [Header("商品类别")]
    public string[] productNames = new string[] {
        "신라면", "진라면", "삼양라면", 
        "사브레", "땅콩샌드", "버터링"
    };
    
    [Header("服务器配置")]
    public string serverUrl = "http://localhost:8008";
    public float requestTimeout = 10f;
    
    [Header("语音配置")]
    public string wakeWord = "시작";
    public int sampleRate = 16000;
    public float recordingLength = 5f;
    
    [Header("AR显示")]
    public float cubeSize = 0.1f;
    public float displayDuration = 10f;
    public Material[] productMaterials; // 为每个商品准备不同材质
}