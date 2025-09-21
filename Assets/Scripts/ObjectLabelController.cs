using TMPro;
using UnityEngine;

namespace Assets.Scripts
{
    /// <summary>
    ///     Manages the visual aspects of the object label.
    /// </summary>
    public class ObjectLabelController : MonoBehaviour
    {
        /// <summary>
        ///     Parent of the displayed label.
        /// </summary>
        public GameObject ContentParent;

        /// <summary>
        ///     Renderer for showing a line between the center of the object and the label.
        /// </summary>
        public LineRenderer LineRenderer;

        /// <summary>
        ///     Text mesh for displaying the class of the object.
        /// </summary>
        public TextMeshPro TextMesh;

        [Header("Cube Visualization")]
        [Tooltip("Base scale for the solid cube that is placed on top of a recognized object.")]
        [SerializeField]
        private float cubeSize = 0.08f;

        [Tooltip("Optional offset that is applied to the solid cube relative to the detection center.")]
        [SerializeField]
        private Vector3 cubeOffset = Vector3.zero;

        [Tooltip("Default color that is used when no class specific color is provided.")]
        [SerializeField]
        private Color cubeDefaultColor = new Color(0.2f, 0.8f, 0.2f, 0.45f);

        private GameObject detectionCube;
        private MeshRenderer detectionCubeRenderer;
        private MaterialPropertyBlock cubePropertyBlock;

        /// <summary>
        ///     Sets the display text.
        /// </summary>
        public string Text
        {
            set => this.TextMesh.text = value;
        }

        private void Awake()
        {
            this.EnsureCubeInitialized();
        }

        /// <summary>
        ///     Updates the position of the object label.
        /// </summary>
        /// <param name="newPosition">New position of the object.</param>
        public void UpdatePosition(Vector3 newPosition)
        {
            this.transform.position = newPosition;

            // update line between text and center of object
            this.LineRenderer.SetPosition(0, this.ContentParent.transform.position);
            this.LineRenderer.SetPosition(1, this.transform.position);
        }

        /// <summary>
        ///     Updates the appearance of the solid cube that visualizes the detected object.
        /// </summary>
        /// <param name="color">Color that should be applied to the cube.</param>
        /// <param name="size">Uniform size of the cube.</param>
        public void UpdateCubeAppearance(Color color, float size)
        {
            this.EnsureCubeInitialized();

            if (this.detectionCube != null)
            {
                this.detectionCube.SetActive(true);
                this.detectionCube.transform.localPosition = this.cubeOffset;
                this.detectionCube.transform.localScale = Vector3.one * Mathf.Max(0.001f, size);
            }

            if (this.detectionCubeRenderer == null)
            {
                return;
            }

            if (this.cubePropertyBlock == null)
            {
                this.cubePropertyBlock = new MaterialPropertyBlock();
            }

            this.cubePropertyBlock.SetColor("_Color", color);
            this.detectionCubeRenderer.SetPropertyBlock(this.cubePropertyBlock);
        }

        /// <summary>
        ///     Hides the visualization cube. This is useful when an item is removed or recycled.
        /// </summary>
        public void HideCube()
        {
            if (this.detectionCube != null)
            {
                this.detectionCube.SetActive(false);
            }
        }

        private void EnsureCubeInitialized()
        {
            if (this.detectionCube != null && this.detectionCubeRenderer != null)
            {
                return;
            }

            Transform existingCube = this.transform.Find("DetectionCube");
            if (existingCube != null)
            {
                this.detectionCube = existingCube.gameObject;
                this.detectionCubeRenderer = this.detectionCube.GetComponent<MeshRenderer>();
            }

            if (this.detectionCube == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "DetectionCube";
                cube.transform.SetParent(this.transform, false);
                cube.transform.localPosition = this.cubeOffset;
                cube.transform.localRotation = Quaternion.identity;
                cube.transform.localScale = Vector3.one * this.cubeSize;

                Collider collider = cube.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                this.detectionCube = cube;
                this.detectionCubeRenderer = cube.GetComponent<MeshRenderer>();
            }

            if (this.detectionCubeRenderer != null)
            {
                if (this.cubePropertyBlock == null)
                {
                    this.cubePropertyBlock = new MaterialPropertyBlock();
                }

                this.cubePropertyBlock.SetColor("_Color", this.cubeDefaultColor);
                this.detectionCubeRenderer.SetPropertyBlock(this.cubePropertyBlock);
                this.detectionCubeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                this.detectionCubeRenderer.receiveShadows = false;
            }
        }
    }
}