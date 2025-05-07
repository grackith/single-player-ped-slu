using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using System.Text.RegularExpressions;
using System.Text;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine.Rendering;

[InitializeOnLoad]
[CustomEditor(typeof(SamplesShowcase))]
public class SamplesShowcaseEditor : Editor
{
    // Define wrapper classes inside SamplesShowcaseEditor
    private class SampleWrapper
    {
        private object originalSample;

        public SampleWrapper(object sample)
        {
            originalSample = sample;
        }

        public string title
        {
            get { return (string)originalSample.GetType().GetProperty("title").GetValue(originalSample, null); }
        }

        public string description
        {
            get { return (string)originalSample.GetType().GetProperty("description").GetValue(originalSample, null); }
        }
    }

    private class SamplesWrapper
    {
        private object wrappedSamples;

        public SamplesWrapper(object samples)
        {
            wrappedSamples = samples;
        }

        public string introduction
        {
            get
            {
                return (string)wrappedSamples.GetType().GetProperty("introduction").GetValue(wrappedSamples, null);
            }
        }

        public SampleWrapper FindSampleWithPrefab(GameObject prefab)
        {
            var method = wrappedSamples.GetType().GetMethod("FindSampleWithPrefab");
            var result = method.Invoke(wrappedSamples, new object[] { prefab });

            if (result == null)
                return null;

            return new SampleWrapper(result);
        }

        public static SamplesWrapper CreateFromJSON(string json, GameObject[] prefabs)
        {
            // Find the Samples type by searching all loaded assemblies
            Type samplesType = null;

            // First try to find the type in the most likely places
            samplesType = Type.GetType("Samples");

            if (samplesType == null)
            {
                // If that fails, search through all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var type = assembly.GetType("Samples");
                        if (type != null)
                        {
                            samplesType = type;
                            break;
                        }

                        // Also check for the fully qualified name
                        type = assembly.GetType("Assembly-CSharp.Samples");
                        if (type != null)
                        {
                            samplesType = type;
                            break;
                        }
                    }
                    catch
                    {
                        // Skip assemblies that cause errors
                        continue;
                    }
                }
            }

            if (samplesType == null)
            {
                Debug.LogError("Could not find the 'Samples' type in any loaded assembly");
                return null;
            }

            var method = samplesType.GetMethod("CreateFromJSON");
            if (method == null)
            {
                Debug.LogError("Could not find the 'CreateFromJSON' method on the Samples type");
                return null;
            }

            var result = method.Invoke(null, new object[] { json, prefabs });
            return new SamplesWrapper(result);
        }
    }

    private static readonly string UXMLPath = "SamplesSelectionUXML";
    public static readonly string[] supportedExtensions = { ".shadergraph", ".vfx", ".cs", ".hlsl", ".shader", ".asset", ".mat", ".fbx", ".prefab" };

    SerializedProperty currentIndex;
    Color headlineColor;
    Color openColor;
    Color highlightColor;
    Color codeColor;

    DropdownField dropdownField;
    List<string> choices;

    int indexSelected;

    private VisualElement requiredSettingsBox;
    private Dictionary<RequiredSettingBase, VisualElement> requiredSettingsVE = new Dictionary<RequiredSettingBase, VisualElement>();

    private void OnEnable()
    {
        SamplesShowcase.OnUpdateSamplesInspector += UpdateSamplesInspector;
        currentIndex = serializedObject.FindProperty("currentIndex");
        RenderPipelineManager.activeRenderPipelineCreated += UpdateRequiredSettingsDisplay;
    }

    private void OnDisable()
    {
        RenderPipelineManager.activeRenderPipelineCreated -= UpdateRequiredSettingsDisplay;
        SamplesShowcase.OnUpdateSamplesInspector -= UpdateSamplesInspector;
    }

    public void UpdateSamplesInspector()
    {
        SamplesShowcase self;
        try
        {
            self = (SamplesShowcase)target;
        }
        catch
        {
            return;
        }

        if (dropdownField != null && self != null)
        {
            dropdownField.value = choices[self.currentIndex]; //make sure samples description is updated from what the scene is showing, even during runtime input or inspector duplication
        }
    }

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        var visualTree = Resources.Load<VisualTreeAsset>(UXMLPath);
        VisualElement inspectorUI = visualTree.CloneTree();
        root.Add(inspectorUI);

        var self = (SamplesShowcase)target;

        //colors
        headlineColor = EditorGUIUtility.isProSkin ? self.headlineDarkColor : self.headlineLightColor;
        openColor = EditorGUIUtility.isProSkin ? self.openDarkColor : self.openLightColor;
        highlightColor = EditorGUIUtility.isProSkin ? self.highlightDarkColor : self.highlightLightColor;
        codeColor = EditorGUIUtility.isProSkin ? self.codeDarkColor : self.codeLightColor;
        root.Q<Label>("headline").style.color = headlineColor;

        bool isTextOnly = self.PresentationMode == SamplesShowcase.Mode.TextOnly ? true : false;
        root.Q<VisualElement>("SamplesSelection").style.display = isTextOnly ? DisplayStyle.None : DisplayStyle.Flex;

        // JSon data of the samples
        if (self.SamplesDescriptionsJson != null)
        {
            string jsonText = SamplesShowcase.CleanupJson(self.SamplesDescriptionsJson.text);
            SamplesWrapper sampleJsonObject = SamplesWrapper.CreateFromJSON(jsonText, self.samplesPrefabs);

            if (sampleJsonObject != null)
            {
                //Introduction, it's the first part of the Samples Description text asset
                var introElement = root.Q<VisualElement>("intro");
                string introText = sampleJsonObject.introduction;
                SamplesShowcase.SanitizedIntroduction = SamplesShowcase.SanitizeText(introText);

                CreateMarkdown(introElement, introText);

                SamplesShowcase.SanitizedDescriptions = new Dictionary<string, string>();
                SamplesShowcase.SanitizedTitles = new Dictionary<string, string>();
                foreach (GameObject prefab in self.samplesPrefabs)
                {
                    SampleWrapper currentSample = sampleJsonObject.FindSampleWithPrefab(prefab);
                    if (currentSample == null)
                        continue;

                    string description = SamplesShowcase.SanitizeText(currentSample.description);
                    SamplesShowcase.SanitizedDescriptions.Add(prefab.name, description);
                    SamplesShowcase.SanitizedTitles.Add(prefab.name, currentSample.title);
                }

                //Create Samples Dropdown
                dropdownField = root.Q<DropdownField>("SampleDropDown");

                if (!isTextOnly && self.samplesPrefabs.Length > 0)
                {
                    dropdownField = root.Q<DropdownField>("SampleDropDown");
                    choices = new List<string>();
                    foreach (GameObject prefab in self.samplesPrefabs)
                    {
                        SampleWrapper sample = sampleJsonObject.FindSampleWithPrefab(prefab);
                        if (sample != null)
                        {
                            choices.Add(sample.title);
                        }
                    }
                    if (currentIndex.intValue >= choices.Count)
                        currentIndex.intValue = choices.Count - 1;

                    dropdownField.value = choices[currentIndex.intValue];
                    dropdownField.choices = choices;
                    GoToSample(self, root, currentIndex.intValue, sampleJsonObject);
                    dropdownField.RegisterValueChangedCallback(v => GoToSample(self, root, choices.IndexOf(dropdownField.value), sampleJsonObject));  //Dropdown function call
                    root.Q<Button>("SelectSampleBtn").style.display = self.enableSelectButton ? DisplayStyle.Flex : DisplayStyle.None;
                    root.Q<Button>("SelectSampleBtn").clicked += () => { Selection.activeGameObject = self.currentPrefab; };

                    //Arrow Button Switch
                    root.Q<Button>("switchBack").clicked += () =>
                    {
                        currentIndex.intValue = currentIndex.intValue == 0 ? self.samplesPrefabs.Length - 1 : currentIndex.intValue - 1;
                        dropdownField.value = choices[currentIndex.intValue];
                    };

                    root.Q<Button>("switchForward").clicked += () =>
                    {
                        currentIndex.intValue = currentIndex.intValue == self.samplesPrefabs.Length - 1 ? 0 : currentIndex.intValue + 1;
                        dropdownField.value = choices[currentIndex.intValue];
                    };
                }
            }
            else
            {
                Debug.LogError("Failed to create SamplesWrapper from JSON");
            }
        }

        requiredSettingsBox = root.Q(name = "RequiredSettingsBox");
        var requiredSettingsList = root.Q(name = "RequiredSettingsList");

        // Hide reference button
        requiredSettingsList.Q(name = null, "requiredSettingButton").style.display = DisplayStyle.None;

        var requiredSettingsSO = (target as SamplesShowcase).requiredSettingsSO;
        if (requiredSettingsSO != null && requiredSettingsSO.requiredSettings != null && requiredSettingsSO.requiredSettings.Count > 0)
        {
            foreach (var setting in requiredSettingsSO.requiredSettings)
            {
                var settingButton = new Button(
                () =>
                {
                    if (string.IsNullOrEmpty(setting.editorAssemblyName) || string.IsNullOrEmpty(setting.editorClassName) || string.IsNullOrEmpty(setting.editorShowFunctionName))
                    {
                        SettingsService.OpenProjectSettings(setting.projectSettingsPath);
                        CoreEditorUtils.Highlight("Project Settings", setting.propertyPath, HighlightSearchMode.Identifier);
                    }
                    else
                    {
                        var editorAssembly = Assembly.Load(setting.editorAssemblyName);
                        var editorClass = editorAssembly.GetType(setting.editorClassName);
                        editorClass.GetMethod(setting.editorShowFunctionName).Invoke(null, new[] { setting });
                    }
                })
                {
                    text = setting.name,
                };

                string description = setting.description;
                if (string.IsNullOrEmpty(description))
                    description = setting.property.tooltip;

                settingButton.tooltip = description;

                settingButton.AddToClassList("requiredSettingButton");
                if (!requiredSettingsVE.ContainsKey(setting))
                    requiredSettingsVE.Add(setting, settingButton);
                requiredSettingsList.Add(settingButton);
            }
        }
        UpdateRequiredSettingsDisplay();

        // The OpenInWindowButton functionality is commented out because we don't know the correct namespace
        // root.Q<Button>(name = "OpenInWindowButton").clicked += () => { EditorWindow.GetWindow<SamplesWindow>("Samples Showcase", true, System.Type.GetType("UnityEditor.InspectorWindow,UnityEditor.dll")); };

        return root;
    }

    void LinkOnPointerUp(PointerUpLinkTagEvent evt)
    {
        if (IsLinkFile(evt.linkID))
        {
            string finalPath = GetAssetFinalPath(evt.linkID);
            var assetpath = AssetDatabase.LoadMainAssetAtPath(finalPath);
            AssetDatabase.OpenAsset(assetpath);
        }
        else
        {
            Ping(evt.linkID);
        }
    }

    void LinkOnPointerOver(PointerOverLinkTagEvent evt)
    {
        var targetVE = (VisualElement)evt.currentTarget;
        targetVE.AddToClassList("link-cursor");
    }

    void LinkOnPointerOut(PointerOutLinkTagEvent evt)
    {
        var targetVE = (VisualElement)evt.currentTarget;
        targetVE.RemoveFromClassList("link-cursor");
    }

    private void UpdateRequiredSettingsDisplay()
    {
        var samplesShowcase = target as SamplesShowcase;
        if (samplesShowcase == null)
            return;

        if (samplesShowcase.requiredSettingsSO == null || samplesShowcase.requiredSettingsSO.requiredSettings == null || samplesShowcase.requiredSettingsSO.requiredSettings.Count == 0)
        {
            requiredSettingsBox.style.display = DisplayStyle.None;
        }

        bool displayBox = false;
        foreach (var settingVEPair in requiredSettingsVE)
        {
            var state = settingVEPair.Key.state;
            displayBox |= !state;
            settingVEPair.Value.style.display = (state) ? DisplayStyle.None : DisplayStyle.Flex;
        }
        requiredSettingsBox.style.display = (displayBox) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void GoToSample(SamplesShowcase self, VisualElement root, int index, SamplesWrapper sampleJsonObject)
    {
        serializedObject.Update();
        currentIndex.intValue = index; //Send the new index value to the monobehaviour
        var sampleInfosElement = root.Q<VisualElement>("sampleInfosContainer");

        // Finding the sample in question
        SampleWrapper currentSample = sampleJsonObject.FindSampleWithPrefab(self.samplesPrefabs[index]);

        string currentSampleText = currentSample.description;
        if (self.gameobjectSamplesName != null)
        {
            self.gameobjectSamplesName.text = currentSample.title;
        }

        if (self.gameobjectSamplesDescription != null)
        {
            self.gameobjectSamplesDescription.text = currentSampleText;
        }

        CreateMarkdown(sampleInfosElement, currentSampleText);
        serializedObject.ApplyModifiedProperties();
    }

    private string CreateMarkdown(VisualElement element, string text)
    {
        if (element != null)
            element.Clear();

        var label = new Label();

        var parsedText = text;

        // Format tags
        // Links
        parsedText = Regex.Replace(parsedText, @"<(link=\""([\s\S]+?)\""[\s\S]*?)>", m =>
        {
            return $"<{m.Groups[1].Value}><color=#{ColorUtility.ToHtmlStringRGBA(IsLinkFile(m.Groups[2].Value) ? openColor : highlightColor)}>";
        });
        parsedText = parsedText.Replace("</link>", "</color></link>");

        // Titles
        parsedText = parsedText.Replace("<h1>", "<b><size=16>");
        parsedText = parsedText.Replace("</h1>", "</b></size>");

        // Code
        parsedText = parsedText.Replace("<code>", $"<color=#{ColorUtility.ToHtmlStringRGBA(codeColor)}><i>");
        parsedText = parsedText.Replace("</code>", "</i></color>");

        // Add tab spacing to lists
        parsedText = Regex.Replace(parsedText, "•.*?(?:<br>|$)", "<margin=1em>$0</margin>");

        // Remove ingore tags
        parsedText = Regex.Replace(parsedText, @"<\/?ignore>", "");

        // Register link callbacks
        label.RegisterCallback<PointerUpLinkTagEvent>(LinkOnPointerUp);
        label.RegisterCallback<PointerOverLinkTagEvent>(LinkOnPointerOver);
        label.RegisterCallback<PointerOutLinkTagEvent>(LinkOnPointerOut);

        label.enableRichText = true;
        label.style.whiteSpace = WhiteSpace.Normal;

        label.text = parsedText;

        if (element != null)
            element.Add(label);

        return parsedText;
    }

    public static bool IsLinkFile(string linkID)
    {
        foreach (string extension in supportedExtensions)
        {
            if (linkID.EndsWith(extension))
                return true;
        }
        return false;
    }

    private static string GetAssetFinalPath(string filename)
    {
        // Splitting the name
        string filenameOnly = System.IO.Path.GetFileNameWithoutExtension(filename);

        // Searching for asset with the filename
        string[] results = AssetDatabase.FindAssets($"{filenameOnly}", null);
        foreach (string result in results)
        {
            string path = AssetDatabase.GUIDToAssetPath(result);
            // Making sure everything matches.
            // /!\ will return the first if there's duplicate
            if (path.EndsWith(filename))
            {
                return path;
            }
        }

        // If the asset hasn't been found display error on opening inspector (easy to spot mistakes all at once)
        Debug.LogError($"Asset {filename} could not be found.");

        return null;
    }

    private static void Ping(string gameObjectName)
    {
        GameObject go = GameObject.Find(gameObjectName);
        UnityEditor.EditorGUIUtility.PingObject(go);
    }
}