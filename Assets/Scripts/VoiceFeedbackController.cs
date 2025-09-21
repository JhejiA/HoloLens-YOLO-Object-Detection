using System;
using UnityEngine;
#if WINDOWS_UWP
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;
#endif

namespace Assets.Scripts
{
    /// <summary>
    ///     Coordinates the voice guided search workflow by combining speech recognition, feedback and YOLO detections.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VoiceFeedbackController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField]
        private VoiceCommandManager voiceCommandManager;

        [SerializeField]
        private YoloRecognitionHandler recognitionHandler;

        [SerializeField]
        private AudioSource audioSource;

        [Header("Speech Content")]
        [SerializeField]
        private bool announceSearchStart = true;

        [SerializeField]
        private string searchStartedFormat = "开始寻找{0}";

        [SerializeField]
        private string targetFoundFormat = "找到了{0}";

        [SerializeField]
        private string targetNotFoundFormat = "当前视野内没有{0}，请到其他位置尝试";

        [Header("Timing")]
        [Tooltip("Delay in seconds before the system informs the user that the target was not found.")]
        [SerializeField]
        private float notFoundAnnouncementDelay = 5f;

        [Tooltip("Minimum time between repeated 'not found' notifications.")]
        [SerializeField]
        private float notFoundRepeatDelay = 12f;

        [Tooltip("Minimum time between repeated 'found' notifications.")]
        [SerializeField]
        private float foundRepeatCooldown = 6f;

        [SerializeField]
        private bool logSpeechInEditor = true;

#if WINDOWS_UWP
        private SpeechSynthesizer speechSynthesizer;
        private Task speechQueue = Task.CompletedTask;
#endif

        private ObjectClass? currentTarget;
        private string currentTargetName;
        private float lastDetectionTime;
        private float lastNotFoundAnnouncement = float.NegativeInfinity;
        private float lastFoundAnnouncement = float.NegativeInfinity;

        private void Awake()
        {
            if (this.voiceCommandManager == null)
            {
                this.voiceCommandManager = FindObjectOfType<VoiceCommandManager>();
            }

            if (this.recognitionHandler == null)
            {
                this.recognitionHandler = GetComponent<YoloRecognitionHandler>();
            }

            if (this.audioSource == null)
            {
                this.audioSource = GetComponent<AudioSource>();
            }

            if (this.audioSource == null)
            {
                this.audioSource = this.gameObject.AddComponent<AudioSource>();
            }

            this.audioSource.playOnAwake = false;
            this.audioSource.loop = false;
        }

        private void OnEnable()
        {
            if (this.voiceCommandManager != null)
            {
                this.voiceCommandManager.SearchRequested += this.HandleSearchRequested;
                this.voiceCommandManager.SearchCleared += this.HandleSearchCleared;
            }

            if (this.recognitionHandler != null)
            {
                this.recognitionHandler.ItemUpdated += this.HandleItemUpdated;
            }
        }

        private void OnDisable()
        {
            if (this.voiceCommandManager != null)
            {
                this.voiceCommandManager.SearchRequested -= this.HandleSearchRequested;
                this.voiceCommandManager.SearchCleared -= this.HandleSearchCleared;
            }

            if (this.recognitionHandler != null)
            {
                this.recognitionHandler.ItemUpdated -= this.HandleItemUpdated;
            }
        }

        private void Update()
        {
            if (!this.currentTarget.HasValue)
            {
                return;
            }

            float timeSinceLastDetection = Time.time - this.lastDetectionTime;
            if (timeSinceLastDetection < this.notFoundAnnouncementDelay)
            {
                return;
            }

            if (Time.time - this.lastNotFoundAnnouncement < this.notFoundRepeatDelay)
            {
                return;
            }

            this.Announce(string.Format(this.targetNotFoundFormat, this.currentTargetName));
            this.lastNotFoundAnnouncement = Time.time;
        }

        /// <summary>
        ///     Starts a search for the given class. This can be used by UI buttons when voice input is not available.
        /// </summary>
        public void StartSearch(ObjectClass target, string displayName = null)
        {
            string resolvedName = displayName;
            if (string.IsNullOrWhiteSpace(resolvedName) && this.voiceCommandManager != null)
            {
                resolvedName = this.voiceCommandManager.GetDisplayName(target);
            }

            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                resolvedName = target.ToString();
            }

            if (this.voiceCommandManager != null)
            {
                this.voiceCommandManager.RequestSearchFor(target, resolvedName);
            }
            else
            {
                this.HandleSearchRequested(target, resolvedName);
            }
        }

        /// <summary>
        ///     Stops the current search.
        /// </summary>
        public void StopSearch()
        {
            if (this.voiceCommandManager != null)
            {
                this.voiceCommandManager.ClearSearch();
            }
            else
            {
                this.HandleSearchCleared();
            }
        }

        private void HandleSearchRequested(ObjectClass targetClass, string displayName)
        {
            string resolvedName = string.IsNullOrWhiteSpace(displayName) ? targetClass.ToString() : displayName;

            this.currentTarget = targetClass;
            this.currentTargetName = resolvedName;
            this.lastDetectionTime = Time.time;
            this.lastNotFoundAnnouncement = float.NegativeInfinity;
            this.lastFoundAnnouncement = float.NegativeInfinity;

            if (this.recognitionHandler != null)
            {
                this.recognitionHandler.SetActiveTarget(targetClass, resolvedName);
            }

            if (this.announceSearchStart)
            {
                this.Announce(string.Format(this.searchStartedFormat, resolvedName));
            }
        }

        private void HandleSearchCleared()
        {
            if (this.recognitionHandler != null)
            {
                this.recognitionHandler.ClearActiveTarget();
            }

            this.currentTarget = null;
            this.currentTargetName = null;
        }

        private void HandleItemUpdated(DisplayedItem item)
        {
            if (!this.currentTarget.HasValue)
            {
                return;
            }

            if (item.YoloItem.MostLikelyClass != this.currentTarget.Value)
            {
                return;
            }

            this.lastDetectionTime = Time.time;
            this.lastNotFoundAnnouncement = float.NegativeInfinity;

            if (Time.time - this.lastFoundAnnouncement < this.foundRepeatCooldown)
            {
                return;
            }

            this.Announce(string.Format(this.targetFoundFormat, this.currentTargetName));
            this.lastFoundAnnouncement = Time.time;
        }

        private void Announce(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

#if WINDOWS_UWP
            this.speechQueue = this.speechQueue.ContinueWith(_ => this.SpeakInternalAsync(message)).Unwrap();
#else
            if (this.logSpeechInEditor)
            {
                Debug.Log($"[VoiceFeedback] {message}");
            }
#endif
        }

#if WINDOWS_UWP
        private async Task SpeakInternalAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (this.speechSynthesizer == null)
            {
                this.speechSynthesizer = new SpeechSynthesizer();
            }

            using (SpeechSynthesisStream speechStream = await this.speechSynthesizer.SynthesizeTextToStreamAsync(message))
            using (DataReader dataReader = new DataReader(speechStream))
            {
                uint size = (uint)speechStream.Size;
                await dataReader.LoadAsync(size);
                byte[] buffer = new byte[size];
                dataReader.ReadBytes(buffer);

                int channelCount = (int)speechStream.AudioEncodingProperties.ChannelCount;
                int sampleRate = (int)speechStream.AudioEncodingProperties.SampleRate;
                int bytesPerSample = speechStream.AudioEncodingProperties.BitsPerSample / 8;
                if (channelCount == 0 || bytesPerSample == 0)
                {
                    return;
                }

                int sampleCount = buffer.Length / bytesPerSample;
                int sampleLength = sampleCount / channelCount;
                float[] samples = new float[sampleCount];
                int sampleIndex = 0;
                for (int i = 0; i < buffer.Length; i += bytesPerSample)
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    samples[sampleIndex++] = sample / 32768f;
                }

                var completion = new TaskCompletionSource<bool>();
                UnityEngine.WSA.Application.InvokeOnAppThread(() =>
                {
                    try
                    {
                        AudioClip clip = AudioClip.Create("VoiceFeedback", sampleLength, channelCount, sampleRate, false);
                        clip.SetData(samples, 0);
                        this.audioSource.Stop();
                        this.audioSource.clip = clip;
                        this.audioSource.Play();
                    }
                    finally
                    {
                        completion.SetResult(true);
                    }
                }, false);

                await completion.Task;
            }
        }
#endif
    }
}
