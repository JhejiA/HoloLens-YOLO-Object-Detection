using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace Assets.Scripts
{
    /// <summary>
    ///     Configuration data for a voice command mapping.
    /// </summary>
    [Serializable]
    public class VoiceCommandMapping
    {
        [Tooltip("Object class that should be triggered by this mapping.")]
        public ObjectClass TargetClass;

        [Tooltip("Friendly name that is used for voice feedback. Leave empty to use the enum name.")]
        public string DisplayName;

        [Tooltip("Phrases that trigger this mapping. They must match the recognition result exactly.")]
        public string[] Phrases;
    }

    /// <summary>
    ///     Handles speech recognition and maps phrases to object classes.
    /// </summary>
    public class VoiceCommandManager : MonoBehaviour
    {
        [Header("Voice Commands")]
        [Tooltip("Custom voice command mappings. Leave empty to rely on the auto generated commands.")]
        [SerializeField]
        private VoiceCommandMapping[] commandMappings = new VoiceCommandMapping[0];

        [Tooltip("Prefixes that are used to auto-generate additional commands such as 'find apple'.")]
        [SerializeField]
        private string[] defaultSearchPrefixes = new[] { "find", "look for", "search for", "i want to find" };

        [Tooltip("Phrases that cancel the current search.")]
        [SerializeField]
        private string[] clearSearchCommands = new[] { "stop search", "cancel search", "clear search" };

        [Tooltip("Automatically create commands for classes that are missing in the mapping list.")]
        [SerializeField]
        private bool autoGenerateMissingMappings = true;

        [Tooltip("Log recognized phrases when running inside the Unity editor.")]
        [SerializeField]
        private bool logRecognizedPhrases = true;

#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_EDITOR_WIN
        [Tooltip("Confidence level that is required before a phrase is accepted.")]
        [SerializeField]
        private ConfidenceLevel recognitionConfidence = ConfidenceLevel.Medium;

        private KeywordRecognizer keywordRecognizer;
#endif

        private readonly Dictionary<string, VoiceCommandMapping> phraseLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> clearLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ObjectClass, VoiceCommandMapping> mappingByClass = new();

        /// <summary>
        ///     Gets the currently active target class that was requested via voice command.
        /// </summary>
        public ObjectClass? ActiveTarget { get; private set; }

        /// <summary>
        ///     Gets the friendly display name of the active target.
        /// </summary>
        public string ActiveTargetDisplayName { get; private set; }

        /// <summary>
        ///     Event that is raised when the user requests a search.
        /// </summary>
        public event Action<ObjectClass, string> SearchRequested;

        /// <summary>
        ///     Event that is raised when the user cancels the current search.
        /// </summary>
        public event Action SearchCleared;

        private void Awake()
        {
            this.BuildLookup();
        }

        private void OnEnable()
        {
            this.StartRecognizer();
        }

        private void OnDisable()
        {
            this.StopRecognizer();
        }

        private void OnDestroy()
        {
            this.StopRecognizer();
        }

        /// <summary>
        ///     Allows external systems to override the command mappings at runtime.
        /// </summary>
        /// <param name="mappings">New voice command mappings.</param>
        public void ConfigureMappings(IEnumerable<VoiceCommandMapping> mappings)
        {
            this.commandMappings = mappings != null ? mappings.ToArray() : new VoiceCommandMapping[0];
            this.BuildLookup();
            this.RestartRecognizerIfNeeded();
        }

        /// <summary>
        ///     Requests a search programmatically. This is useful for UI buttons.
        /// </summary>
        /// <param name="targetClass">Class that should be searched.</param>
        /// <param name="displayName">Optional friendly name.</param>
        public void RequestSearchFor(ObjectClass targetClass, string displayName = null)
        {
            this.EnsureMappings();

            VoiceCommandMapping mapping;
            if (!this.mappingByClass.TryGetValue(targetClass, out mapping))
            {
                mapping = this.CreateDefaultMapping(targetClass);
                this.mappingByClass[targetClass] = mapping;
            }

            string resolvedName = string.IsNullOrWhiteSpace(displayName)
                ? this.ResolveDisplayName(mapping)
                : displayName.Trim();

            this.ActiveTarget = targetClass;
            this.ActiveTargetDisplayName = resolvedName;
            this.SearchRequested?.Invoke(targetClass, resolvedName);
        }

        /// <summary>
        ///     Clears the current search request.
        /// </summary>
        public void ClearSearch()
        {
            if (!this.ActiveTarget.HasValue)
            {
                return;
            }

            this.ActiveTarget = null;
            this.ActiveTargetDisplayName = null;
            this.SearchCleared?.Invoke();
        }

        /// <summary>
        ///     Returns the friendly display name for a given class.
        /// </summary>
        /// <param name="objectClass">Class that should be resolved.</param>
        /// <returns>Friendly name.</returns>
        public string GetDisplayName(ObjectClass objectClass)
        {
            this.EnsureMappings();

            VoiceCommandMapping mapping;
            if (this.mappingByClass.TryGetValue(objectClass, out mapping))
            {
                return this.ResolveDisplayName(mapping);
            }

            return this.CreateFriendlyName(objectClass.ToString());
        }

        private void BuildLookup()
        {
            this.phraseLookup.Clear();
            this.clearLookup.Clear();
            this.mappingByClass.Clear();

            if (this.commandMappings != null)
            {
                foreach (VoiceCommandMapping mapping in this.commandMappings)
                {
                    this.RegisterMapping(mapping);
                }
            }

            if (this.autoGenerateMissingMappings)
            {
                foreach (ObjectClass objectClass in Enum.GetValues(typeof(ObjectClass)))
                {
                    if (this.mappingByClass.ContainsKey(objectClass))
                    {
                        continue;
                    }

                    VoiceCommandMapping mapping = this.CreateDefaultMapping(objectClass);
                    this.RegisterMapping(mapping);
                }
            }

            if (this.clearSearchCommands != null)
            {
                foreach (string command in this.clearSearchCommands)
                {
                    string normalized = this.NormalizePhrase(command);
                    if (string.IsNullOrEmpty(normalized))
                    {
                        continue;
                    }

                    this.clearLookup.Add(normalized);
                }
            }
        }

        private void RegisterMapping(VoiceCommandMapping mapping)
        {
            if (mapping == null)
            {
                return;
            }

            mapping.DisplayName = this.ResolveDisplayName(mapping);
            this.mappingByClass[mapping.TargetClass] = mapping;

            bool hasCustomPhrase = false;
            if (mapping.Phrases != null)
            {
                foreach (string phrase in mapping.Phrases)
                {
                    string normalized = this.NormalizePhrase(phrase);
                    if (string.IsNullOrEmpty(normalized))
                    {
                        continue;
                    }

                    this.phraseLookup[normalized] = mapping;
                    hasCustomPhrase = true;
                }
            }

            string displayPhrase = this.NormalizePhrase(mapping.DisplayName);
            if (!string.IsNullOrEmpty(displayPhrase))
            {
                this.phraseLookup[displayPhrase] = mapping;
            }

            if (!hasCustomPhrase && this.autoGenerateMissingMappings)
            {
                this.RegisterGeneratedPhrases(mapping, mapping.DisplayName);
            }
            else if (mapping.Phrases != null && mapping.Phrases.Length > 0)
            {
                this.RegisterGeneratedPhrases(mapping, mapping.DisplayName);
            }
        }

        private void RegisterGeneratedPhrases(VoiceCommandMapping mapping, string basePhrase)
        {
            if (this.defaultSearchPrefixes == null)
            {
                return;
            }

            foreach (string prefix in this.defaultSearchPrefixes)
            {
                string generated = this.NormalizePhrase(string.Format("{0} {1}", prefix, basePhrase));
                if (string.IsNullOrEmpty(generated))
                {
                    continue;
                }

                if (!this.phraseLookup.ContainsKey(generated))
                {
                    this.phraseLookup.Add(generated, mapping);
                }
            }
        }

        private VoiceCommandMapping CreateDefaultMapping(ObjectClass objectClass)
        {
            string displayName = this.CreateFriendlyName(objectClass.ToString());

            List<string> phrases = new();
            phrases.Add(displayName);

            if (this.defaultSearchPrefixes != null)
            {
                foreach (string prefix in this.defaultSearchPrefixes)
                {
                    phrases.Add(string.Format("{0} {1}", prefix, displayName));
                }
            }

            return new VoiceCommandMapping
            {
                TargetClass = objectClass,
                DisplayName = displayName,
                Phrases = phrases.ToArray()
            };
        }

        private string ResolveDisplayName(VoiceCommandMapping mapping)
        {
            if (mapping == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(mapping.DisplayName))
            {
                return mapping.DisplayName.Trim();
            }

            return this.CreateFriendlyName(mapping.TargetClass.ToString());
        }

        private string CreateFriendlyName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return string.Empty;
            }

            string withSpaces = Regex.Replace(rawName, "([a-z])([A-Z])", "$1 $2");
            withSpaces = withSpaces.Replace('_', ' ');
            return withSpaces.Trim();
        }

        private string NormalizePhrase(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase))
            {
                return string.Empty;
            }

            return phrase.Trim().ToLowerInvariant();
        }

        private void EnsureMappings()
        {
            if (this.mappingByClass.Count == 0 && (this.commandMappings == null || this.commandMappings.Length == 0))
            {
                this.BuildLookup();
            }
        }

        private void RestartRecognizerIfNeeded()
        {
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_EDITOR_WIN
            if (this.keywordRecognizer != null)
            {
                bool wasRunning = this.keywordRecognizer.IsRunning;
                this.StopRecognizer();
                if (wasRunning)
                {
                    this.StartRecognizer();
                }
            }
#endif
        }

        private void StartRecognizer()
        {
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_EDITOR_WIN
            if (this.keywordRecognizer != null)
            {
                if (!this.keywordRecognizer.IsRunning)
                {
                    this.keywordRecognizer.Start();
                }

                return;
            }

            if (this.phraseLookup.Count == 0 && this.clearLookup.Count == 0)
            {
                if (Application.isEditor && this.logRecognizedPhrases)
                {
                    Debug.LogWarning($"{nameof(VoiceCommandManager)} has no voice commands configured.");
                }

                return;
            }

            string[] keywords = this.phraseLookup.Keys.Concat(this.clearLookup).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            this.keywordRecognizer = new KeywordRecognizer(keywords, this.recognitionConfidence);
            this.keywordRecognizer.OnPhraseRecognized += this.HandlePhraseRecognized;
            this.keywordRecognizer.Start();
#else
            if (Application.isEditor && this.logRecognizedPhrases)
            {
                Debug.LogWarning($"{nameof(VoiceCommandManager)} voice recognition is only available on Windows-based targets.");
            }
#endif
        }

        private void StopRecognizer()
        {
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_EDITOR_WIN
            if (this.keywordRecognizer == null)
            {
                return;
            }

            if (this.keywordRecognizer.IsRunning)
            {
                this.keywordRecognizer.Stop();
            }

            this.keywordRecognizer.OnPhraseRecognized -= this.HandlePhraseRecognized;
            this.keywordRecognizer.Dispose();
            this.keywordRecognizer = null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_EDITOR_WIN
        private void HandlePhraseRecognized(PhraseRecognizedEventArgs args)
        {
            string normalized = this.NormalizePhrase(args.text);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            if (this.clearLookup.Contains(normalized))
            {
                if (this.logRecognizedPhrases)
                {
                    Debug.Log($"[VoiceCommandManager] Clear command recognized: {args.text}");
                }

                this.ClearSearch();
                return;
            }

            VoiceCommandMapping mapping;
            if (!this.phraseLookup.TryGetValue(normalized, out mapping))
            {
                return;
            }

            if (this.logRecognizedPhrases)
            {
                Debug.Log($"[VoiceCommandManager] Recognized search command: {args.text}");
            }

            this.ActiveTarget = mapping.TargetClass;
            this.ActiveTargetDisplayName = this.ResolveDisplayName(mapping);
            this.SearchRequested?.Invoke(mapping.TargetClass, this.ActiveTargetDisplayName);
        }
#endif
    }
}
