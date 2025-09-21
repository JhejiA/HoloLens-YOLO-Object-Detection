using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts
{
    /// <summary>
    ///     Handles the recognitions of the yolo model.
    /// </summary>
    public class YoloRecognitionHandler : MonoBehaviour
    {
        private readonly List<DisplayedItem> yoloItems = new();

        [SerializeField]
        private GameObject labelObject;

        [Header("Voice Guided Search")]
        [Tooltip("When enabled the visualizers will only show the detections that match the currently active target class.")]
        [SerializeField]
        private bool restrictDetectionsToActiveTarget = true;

        [Tooltip("Highlight color that is applied to the cube when the detected item matches the requested target.")]
        [SerializeField]
        private Color targetHighlightColor = new Color(1f, 0.85f, 0.2f, 0.65f);

        [Tooltip("Scale multiplier that is applied to the cube when the detected item matches the requested target.")]
        [SerializeField]
        private float targetCubeScaleMultiplier = 1.2f;

        [Header("Cube Visualization")]
        [Tooltip("Base cube size that will be used for all detections unless a class specific override is configured.")]
        [SerializeField]
        private float baseCubeSize = 0.08f;

        [Tooltip("Default cube color that is used when no class specific override is available.")]
        [SerializeField]
        private Color defaultCubeColor = new Color(0.16f, 0.74f, 0.88f, 0.45f);

        [SerializeField]
        private ClassVisualizationSetting[] classVisualization = new ClassVisualizationSetting[0];

        private readonly Dictionary<ObjectClass, ClassVisualizationSetting> classVisualizationLookup = new();

        private YoloDebugOutput yoloDebugOutput;
        private ObjectClass? activeTargetClass;
        private string activeTargetDisplayName;
        private bool targetSeenInLastUpdate;

        /// <summary>
        ///     Event that is raised whenever an item is updated and has reached the minimum visibility threshold.
        /// </summary>
        public event Action<DisplayedItem> ItemUpdated;

        /// <summary>
        ///     Gets the currently active target class that is requested via voice search.
        /// </summary>
        public ObjectClass? ActiveTargetClass => this.activeTargetClass;

        /// <summary>
        ///     Gets the friendly display name of the active target class.
        /// </summary>
        public string ActiveTargetDisplayName => this.activeTargetDisplayName;

        /// <summary>
        ///     Tells whether the currently active target class was visible during the last update.
        /// </summary>
        public bool IsActiveTargetVisible => this.targetSeenInLastUpdate;

        /// <summary>
        ///     Provides read only access to the tracked items.
        /// </summary>
        public IReadOnlyList<DisplayedItem> DisplayedItems => this.yoloItems;

        [Serializable]
        private struct ClassVisualizationSetting
        {
            public ObjectClass TargetClass;
            public Color CubeColor;
            public float SizeMultiplier;
        }

        private void Start()
        {
            this.CacheClassVisualization();
            this.yoloDebugOutput = gameObject.GetComponent<YoloDebugOutput>();
        }

        private void OnValidate()
        {
            this.CacheClassVisualization();
        }

        /// <summary>
        ///     Post process the recognitions and show them.
        /// </summary>
        /// <param name="recognitions">Recognitions of the model.</param>
        /// <param name="cameraTransform">The current camera position.</param>
        public void ShowRecognitions(List<YoloItem> recognitions, CameraTransform cameraTransform)
        {
            List<YoloItem> filteredRecognitions = this.FilterRecognitions(recognitions);

            this.AddNewlyRecognizedObjects(filteredRecognitions, cameraTransform);
            this.RemoveOutdatedObjects();
            this.TriggerDetectionActions();
        }

        /// <summary>
        ///     Sets the currently active target class that should be highlighted.
        /// </summary>
        /// <param name="targetClass">Target class to search for.</param>
        /// <param name="displayName">Friendly display name used for logging.</param>
        public void SetActiveTarget(ObjectClass targetClass, string displayName)
        {
            if (this.activeTargetClass.HasValue && this.activeTargetClass.Value == targetClass)
            {
                this.activeTargetDisplayName = displayName;
                return;
            }

            this.activeTargetClass = targetClass;
            this.activeTargetDisplayName = displayName;
            this.targetSeenInLastUpdate = false;
            this.ClearTrackedItems();
        }

        /// <summary>
        ///     Clears the currently active target and shows all detections again.
        /// </summary>
        public void ClearActiveTarget()
        {
            if (!this.activeTargetClass.HasValue)
            {
                return;
            }

            this.activeTargetClass = null;
            this.activeTargetDisplayName = null;
            this.targetSeenInLastUpdate = false;
            this.ClearTrackedItems();
        }

        private void AddNewlyRecognizedObjects(List<YoloItem> recognitions, CameraTransform cameraTransform)
        {
            List<DisplayedItem> unmatchedExistingItems = new(this.yoloItems);
            foreach (YoloItem newItem in recognitions)
            {
                // Calculate center point of object in space
                Vector3? positionInSpace = PositionCalculator.CalculatePointInSpace(newItem, cameraTransform);
                if (positionInSpace == null)
                {
                    continue;
                }

                // Create new item or update closest, existing item
                DisplayedItem item = this.GetClosestExistingItem(unmatchedExistingItems, newItem, positionInSpace.Value);
                if (item == null)
                {
                    item = new DisplayedItem(newItem, positionInSpace.Value);
                    this.yoloItems.Add(item);
                }
                else
                {
                    unmatchedExistingItems.Remove(item);
                    item.UpdateItem(newItem, positionInSpace.Value);
                }
            }
        }

        /// <summary>
        /// Checks if given item has been seen previously.
        /// If so, returns the closest item of the same class.
        /// </summary>
        /// <param name="oldItems">All previously recognized objects that are unmatched.</param>
        /// <param name="item">The newly recognized item.</param>
        /// <param name="positionInSpace">The position in space of the new item.</param>
        /// <returns>Closest existing item.</returns>
        private DisplayedItem GetClosestExistingItem(List<DisplayedItem> oldItems, YoloItem item, Vector3 positionInSpace)
        {
            DisplayedItem closestItem = null;
            float closestDist = float.MaxValue;

            // Find item of the same class that is closest to the new object, below a certain threshold
            foreach (DisplayedItem oldItem in oldItems)
            {
                if (!oldItem.YoloItem.MostLikelyClass.Equals(item.MostLikelyClass))
                {
                    continue;
                }

                float distance = Vector3.Distance(oldItem.PositionInSpace, positionInSpace);

                if (distance > Parameters.MaxIdenticalObject || distance >= closestDist)
                {
                    continue;
                }

                //Update closest element
                closestItem = oldItem;
                closestDist = distance;
            }

            return closestItem;
        }

        private void RemoveOutdatedObjects()
        {
            for (int i = this.yoloItems.Count - 1; i >= 0; i--)
            {
                bool wasInCameraView = this.yoloItems[i].IsInCameraView;
                bool isInCameraView = PositionCalculator.IsObjectInCameraView(this.yoloItems[i].PositionInSpace);
                this.yoloItems[i].IsInCameraView = isInCameraView;

                if (!isInCameraView)
                {
                    continue;
                }

                if (!wasInCameraView)
                {
                    // Reset time last seen, so that the object is not removed immediately when it is in the camera view again.
                    this.yoloItems[i].TimeLastSeen = Time.time;
                    continue;
                }

                // Remove object if it is not visible anymore for a certain time.
                if (Time.time - this.yoloItems[i].TimeLastSeen <= Parameters.ObjectTimeOut)
                {
                    continue;
                }

                if (yoloItems[i].TrackingMarker != null)
                {
                    ObjectLabelController controller = yoloItems[i].TrackingMarker.GetComponent<ObjectLabelController>();
                    if (controller != null)
                    {
                        controller.HideCube();
                    }

                    Destroy(yoloItems[i].TrackingMarker);
                }

                this.yoloItems.RemoveAt(i);
            }
        }

        private void TriggerDetectionActions()
        {
            bool targetSeenThisFrame = false;

            // Only apply actions if item have been seen multiple times.
            foreach (DisplayedItem item in this.yoloItems.Where(displayedItem => displayedItem.IsInCameraView && displayedItem.TimesSeen >= Parameters.MinTimesSeen))
            {
                if (this.activeTargetClass.HasValue && item.YoloItem.MostLikelyClass == this.activeTargetClass.Value)
                {
                    targetSeenThisFrame = true;
                }

                // Show marker
                this.ManageTrackingMarker(item);

                // Show debug information
                if (this.yoloDebugOutput != null)
                {
                    this.yoloDebugOutput.ShowDebugInformationForItem(item);
                }

                this.ItemUpdated?.Invoke(item);
            }

            this.targetSeenInLastUpdate = this.activeTargetClass.HasValue && targetSeenThisFrame;
        }

        /// <summary>
        ///     Create debug marker if it does not exist or move it to the correct position
        /// </summary>
        /// <param name="item">Item whose marker should be managed</param>
        private void ManageTrackingMarker(DisplayedItem item)
        {
            if (item.TrackingMarker == null)
            {
                item.TrackingMarker = Instantiate(this.labelObject, item.PositionInSpace, Quaternion.identity);
            }

            ObjectLabelController labelController = item.TrackingMarker.GetComponent<ObjectLabelController>();
            labelController.Text = $"{item.YoloItem.MostLikelyClass} ({Math.Round(item.YoloItem.Confidence * 100, 3)}%)";
            labelController.UpdatePosition(item.PositionInSpace);

            Color cubeColor = this.ResolveCubeColor(item.YoloItem.MostLikelyClass);
            float cubeSize = this.ResolveCubeSize(item.YoloItem.MostLikelyClass);

            if (this.activeTargetClass.HasValue && item.YoloItem.MostLikelyClass == this.activeTargetClass.Value)
            {
                cubeColor = this.targetHighlightColor;
                cubeSize *= this.targetCubeScaleMultiplier;
            }

            labelController.UpdateCubeAppearance(cubeColor, cubeSize);
        }

        private List<YoloItem> FilterRecognitions(List<YoloItem> recognitions)
        {
            if (!this.restrictDetectionsToActiveTarget || !this.activeTargetClass.HasValue)
            {
                return recognitions;
            }

            return recognitions.Where(item => item.MostLikelyClass == this.activeTargetClass.Value).ToList();
        }

        private void ClearTrackedItems()
        {
            foreach (DisplayedItem item in this.yoloItems)
            {
                if (item.TrackingMarker != null)
                {
                    Destroy(item.TrackingMarker);
                }
            }

            this.yoloItems.Clear();
        }

        private void CacheClassVisualization()
        {
            this.classVisualizationLookup.Clear();

            if (this.classVisualization == null)
            {
                return;
            }

            foreach (ClassVisualizationSetting setting in this.classVisualization)
            {
                ClassVisualizationSetting sanitized = this.SanitizeSetting(setting);
                this.classVisualizationLookup[sanitized.TargetClass] = sanitized;
            }
        }

        private ClassVisualizationSetting SanitizeSetting(ClassVisualizationSetting setting)
        {
            ClassVisualizationSetting sanitized = setting;
            if (sanitized.SizeMultiplier <= 0f)
            {
                sanitized.SizeMultiplier = 1f;
            }

            return sanitized;
        }

        private Color ResolveCubeColor(ObjectClass objectClass)
        {
            ClassVisualizationSetting setting;
            if (this.classVisualizationLookup.TryGetValue(objectClass, out setting) && setting.CubeColor.a > 0.001f)
            {
                return setting.CubeColor;
            }

            return this.defaultCubeColor;
        }

        private float ResolveCubeSize(ObjectClass objectClass)
        {
            ClassVisualizationSetting setting;
            if (this.classVisualizationLookup.TryGetValue(objectClass, out setting))
            {
                return this.baseCubeSize * setting.SizeMultiplier;
            }

            return this.baseCubeSize;
        }
    }
}

