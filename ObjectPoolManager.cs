using System.Collections.Generic;
using UnityEngine;
using Microsoft.MixedReality.Toolkit.SpatialManipulation;
using Microsoft.MixedReality.OpenXR;

public class ObjectPoolManager : MonoBehaviour
{
    [SerializeField] private GameObject cubePrefab;
    [SerializeField] private int poolSize = 30;
    [SerializeField] private Parameters parameters;
    
    private Queue<GameObject> pool = new Queue<GameObject>();
    private Dictionary<GameObject, float> activeObjects = new Dictionary<GameObject, float>();
    
    void Start()
    {
        InitializePool();
    }
    
    void Update()
    {
        // 回收超时的对象
        var toRemove = new List<GameObject>();
        foreach (var kvp in activeObjects)
        {
            if (Time.time - kvp.Value > parameters.displayDuration)
            {
                toRemove.Add(kvp.Key);
            }
        }
        
        foreach (var obj in toRemove)
        {
            RecycleCube(obj);
        }
    }
    
    private void InitializePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject cube = CreateCube();
            cube.SetActive(false);
            pool.Enqueue(cube);
        }
    }
    
    private GameObject CreateCube()
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = Vector3.one * parameters.cubeSize;
        
        // 添加MRTK组件以支持交互
        cube.AddComponent<ObjectManipulator>();
        cube.AddComponent<NearInteractionGrabbable>();
        
        // 使用Unlit材质提高性能
        var renderer = cube.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Unlit/Color"));
        
        return cube;
    }
    
    public void SpawnCube(string productName, Vector3 position, float confidence)
    {
        GameObject cube = pool.Count > 0 ? pool.Dequeue() : CreateCube();
        
        cube.transform.position = position;
        cube.SetActive(true);
        
        // 设置颜色
        int productIndex = System.Array.IndexOf(parameters.productNames, productName);
        if (productIndex >= 0 && parameters.productMaterials != null && 
            productIndex < parameters.productMaterials.Length)
        {
            cube.GetComponent<Renderer>().material = parameters.productMaterials[productIndex];
        }
        
        // 添加空间锚点（使用MRTK的方式）
        var anchor = cube.AddComponent<SpatialAnchor>();
        
        // 添加标签
        var label = cube.GetComponentInChildren<TextMesh>();
        if (label == null)
        {
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.parent = cube.transform;
            labelObj.transform.localPosition = Vector3.up * 0.15f;
            label = labelObj.AddComponent<TextMesh>();
            label.fontSize = 20;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
        }
        label.text = $"{productName}\n{confidence:P0}";
        
        activeObjects[cube] = Time.time;
    }
    
    private void RecycleCube(GameObject cube)
    {
        activeObjects.Remove(cube);
        
        // 移除空间锚点
        var anchor = cube.GetComponent<SpatialAnchor>();
        if (anchor != null)
        {
            Destroy(anchor);
        }
        
        cube.SetActive(false);
        pool.Enqueue(cube);
    }
    
    public void ClearAll()
    {
        var allActive = new List<GameObject>(activeObjects.Keys);
        foreach (var obj in allActive)
        {
            RecycleCube(obj);
        }
    }
}
