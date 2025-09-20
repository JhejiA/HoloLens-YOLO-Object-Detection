using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.Windows.WebCam;

public class YoloDetector : MonoBehaviour
{
    [SerializeField] private Parameters parameters;
    [SerializeField] private ObjectPoolManager poolManager;
    
    private Model runtimeModel;
    private IWorker worker;
    
    private PhotoCapture photoCaptureObject;
    private Texture2D targetTexture;
    private bool isDetectionEnabled = false;
    private string currentTargetProduct = "";
    
    private int frameCounter = 0;
    private float lastDetectionTime = 0f;
    
    // 检测结果
    public class Detection
    {
        public Rect boundingBox;
        public string className;
        public float confidence;
        public Vector3 worldPosition;
    }
    
    void Start()
    {
        InitializeModel();
        InitializeCamera();
    }
    
    void Update()
    {
        if (!isDetectionEnabled) return;
        
        frameCounter++;
        if (frameCounter % parameters.frameSkipInterval != 0) return;
        
        if (Time.time - lastDetectionTime > 0.1f) // 最小间隔100ms
        {
            CaptureAndDetect();
            lastDetectionTime = Time.time;
        }
    }
    
    private void InitializeModel()
    {
        try
        {
            string modelPath = Application.streamingAssetsPath + "/Models/" + 
                             (parameters.useInt8Model ? parameters.int8ModelPath : parameters.modelPath);
            
            runtimeModel = ModelLoader.Load(modelPath);
            
            // HoloLens 2上使用CPU后端更稳定
            worker = WorkerFactory.CreateWorker(BackendType.CPU, runtimeModel);
            
            Debug.Log($"[YOLO] 模型加载成功: {modelPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[YOLO] 模型加载失败: {e.Message}");
        }
    }
    
    private void InitializeCamera()
    {
        Resolution cameraResolution = PhotoCapture.SupportedResolutions
            .OrderByDescending((res) => res.width * res.height)
            .First();
        
        targetTexture = new Texture2D(cameraResolution.width, cameraResolution.height);
        
        PhotoCapture.CreateAsync(false, delegate(PhotoCapture captureObject)
        {
            photoCaptureObject = captureObject;
            
            CameraParameters cameraParameters = new CameraParameters();
            cameraParameters.hologramOpacity = 0.0f;
            cameraParameters.cameraResolutionWidth = cameraResolution.width;
            cameraParameters.cameraResolutionHeight = cameraResolution.height;
            cameraParameters.pixelFormat = CapturePixelFormat.BGRA32;
            
            captureObject.StartPhotoModeAsync(cameraParameters, delegate(PhotoCapture.PhotoCaptureResult result)
            {
                if (result.success)
                {
                    Debug.Log("[YOLO] 相机初始化成功");
                }
            });
        });
    }
    
    private void CaptureAndDetect()
    {
        if (photoCaptureObject == null) return;
        
        photoCaptureObject.TakePhotoAsync((result, photoCaptureFrame) =>
        {
            if (result.success && photoCaptureFrame != null)
            {
                // 获取相机到世界矩阵
                Matrix4x4 cameraToWorldMatrix;
                photoCaptureFrame.TryGetCameraToWorldMatrix(out cameraToWorldMatrix);
                
                // 获取投影矩阵
                Matrix4x4 projectionMatrix;
                photoCaptureFrame.TryGetProjectionMatrix(out projectionMatrix);
                
                // 复制图像数据
                List<byte> imageBufferList = new List<byte>();
                photoCaptureFrame.CopyRawImageDataIntoBuffer(imageBufferList);
                
                // 在主线程处理
                UnityEngine.WSA.Application.InvokeOnUIThread(() =>
                {
                    ProcessImage(imageBufferList.ToArray(), cameraToWorldMatrix, projectionMatrix);
                }, false);
            }
        });
    }
    
    private void ProcessImage(byte[] imageData, Matrix4x4 cameraToWorld, Matrix4x4 projection)
    {
        // 转换为Texture2D
        targetTexture.LoadRawTextureData(imageData);
        targetTexture.Apply();
        
        // 预处理图像
        var inputTensor = PreprocessImage(targetTexture);
        
        // 运行推理
        worker.Execute(inputTensor);
        var output = worker.PeekOutput() as TensorFloat;
        
        // 后处理
        var detections = PostprocessOutput(output, cameraToWorld, projection);
        
        // 显示检测结果
        DisplayDetections(detections);
        
        // 清理
        inputTensor?.Dispose();
        output?.Dispose();
    }
    
    private TensorFloat PreprocessImage(Texture2D texture)
    {
        // 缩放到模型输入尺寸
        int targetSize = parameters.imageSize;
        RenderTexture rt = RenderTexture.GetTemporary(targetSize, targetSize);
        Graphics.Blit(texture, rt);
        
        RenderTexture.active = rt;
        Texture2D resized = new Texture2D(targetSize, targetSize, TextureFormat.RGB24, false);
        resized.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
        resized.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        
        // 转换为张量
        var pixels = resized.GetPixels32();
        float[] tensorData = new float[3 * targetSize * targetSize];
        
        for (int y = 0; y < targetSize; y++)
        {
            for (int x = 0; x < targetSize; x++)
            {
                int pixelIndex = y * targetSize + x;
                Color32 pixel = pixels[pixelIndex];
                
                // 归一化并按CHW格式存储
                tensorData[0 * targetSize * targetSize + pixelIndex] = pixel.r / 255f;
                tensorData[1 * targetSize * targetSize + pixelIndex] = pixel.g / 255f;
                tensorData[2 * targetSize * targetSize + pixelIndex] = pixel.b / 255f;
            }
        }
        
        Destroy(resized);
        
        return new TensorFloat(new TensorShape(1, 3, targetSize, targetSize), tensorData);
    }
    
    private List<Detection> PostprocessOutput(TensorFloat output, Matrix4x4 cameraToWorld, Matrix4x4 projection)
    {
        var detections = new List<Detection>();
        
        // YOLOv11输出格式: [1, 84, 8400] 或 [1, 4+classes, anchors]
        // 前4个是bbox (cx, cy, w, h)，后面是类别概率
        
        int numClasses = parameters.productNames.Length;
        int numAnchors = output.shape[2];
        
        for (int i = 0; i < numAnchors; i++)
        {
            // 获取置信度最高的类别
            float maxConf = 0;
            int bestClass = -1;
            
            for (int c = 0; c < numClasses; c++)
            {
                float conf = output[0, 4 + c, i];
                if (conf > maxConf)
                {
                    maxConf = conf;
                    bestClass = c;
                }
            }
            
            // 置信度阈值过滤
            if (maxConf < parameters.confidenceThreshold) continue;
            
            // 目标产品过滤
            if (!string.IsNullOrEmpty(currentTargetProduct))
            {
                if (parameters.productNames[bestClass] != currentTargetProduct)
                    continue;
            }
            
            // 提取边界框
            float cx = output[0, 0, i];
            float cy = output[0, 1, i];
            float w = output[0, 2, i];
            float h = output[0, 3, i];
            
            // 转换为屏幕坐标
            Rect bbox = new Rect(
                (cx - w/2) * parameters.imageSize,
                (cy - h/2) * parameters.imageSize,
                w * parameters.imageSize,
                h * parameters.imageSize
            );
            
            // 计算3D世界位置
            Vector3 worldPos = CalculateWorldPosition(bbox, cameraToWorld, projection);
            
            detections.Add(new Detection
            {
                boundingBox = bbox,
                className = parameters.productNames[bestClass],
                confidence = maxConf,
                worldPosition = worldPos
            });
        }
        
        // NMS去重
        return ApplyNMS(detections);
    }
    
    private List<Detection> ApplyNMS(List<Detection> detections)
    {
        var result = new List<Detection>();
        var sorted = detections.OrderByDescending(d => d.confidence).ToList();
        
        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);
            
            sorted.RemoveAll(d => 
                d.className == best.className && 
                CalculateIOU(best.boundingBox, d.boundingBox) > parameters.nmsThreshold
            );
        }
        
        return result;
    }
    
    private float CalculateIOU(Rect a, Rect b)
    {
        float intersectionArea = 0;
        if (a.Overlaps(b))
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            intersectionArea = (xMax - xMin) * (yMax - yMin);
        }
        
        float unionArea = a.width * a.height + b.width * b.height - intersectionArea;
        return intersectionArea / unionArea;
    }
    
    private Vector3 CalculateWorldPosition(Rect bbox, Matrix4x4 cameraToWorld, Matrix4x4 projection)
    {
        // 边界框中心的归一化坐标
        float normalizedX = bbox.center.x / parameters.imageSize;
        float normalizedY = bbox.center.y / parameters.imageSize;
        
        // 反投影到相机空间
        Vector4 clipSpace = new Vector4(
            normalizedX * 2 - 1,
            1 - normalizedY * 2,
            0.5f, // 假设深度
            1
        );
        
        Vector4 viewSpace = projection.inverse * clipSpace;
        viewSpace /= viewSpace.w;
        
        // 转换到世界空间
        Vector4 worldSpace = cameraToWorld * viewSpace;
        
        return new Vector3(worldSpace.x, worldSpace.y, worldSpace.z);
    }
    
    private void DisplayDetections(List<Detection> detections)
    {
        foreach (var detection in detections)
        {
            poolManager.SpawnCube(detection.className, detection.worldPosition, detection.confidence);
            
            // 如果找到目标产品，切换状态
            if (detection.className == currentTargetProduct)
            {
                var fsm = GetComponent<ApplicationFSM>();
                fsm.TransitionTo(AppState.FOUND);
                break;
            }
        }
    }
    
    public void SetDetectionEnabled(bool enabled)
    {
        isDetectionEnabled = enabled;
        Debug.Log($"[YOLO] 检测状态: {enabled}");
    }
    
    public void SetTargetProduct(string productName)
    {
        currentTargetProduct = productName;
        Debug.Log($"[YOLO] 目标产品: {productName}");
    }
    
    void OnDestroy()
    {
        worker?.Dispose();
        
        if (photoCaptureObject != null)
        {
            photoCaptureObject.StopPhotoModeAsync(delegate(PhotoCapture.PhotoCaptureResult result)
            {
                photoCaptureObject.Dispose();
                photoCaptureObject = null;
            });
        }
    }
}