/*
MIT License

Copyright (c) 2021 Rito15

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Copyright (c) 2022 LiveMolo

The modified and added parts of the code are not subject to the same MIT license. (2022.04.18 Ra)
*/

#if UNITY_EDITOR
#define DEBUG_ON
#define UNITY_EDITOR_OPTION
#endif

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
#endif

public class CharacterEffectTool : MonoBehaviour
{
    public bool isEditMode = false;
    public float timeScale = 1f;
    public float timeScaleEdit = 0f;
    public int currentFrameInt;

    public List<EventBundle> bundles = new List<EventBundle>();

    private Animator animator;
    private float currentFrame;

    private static CharacterEffectTool timeController;

    private AnimationClip[] AllAnimationClips => animator.runtimeAnimatorController.animationClips;

    private void Awake() {
        animator = GetComponent<Animator>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(AnimatorIsNotValid()){
            return;
        }

        if(timeController == this){ 
            if(editor_waitTime < EDITOR_WAITTIME_MAX){
                Time.timeScale = 1f;
                editor_waitTime++;
            }
            else {
                Time.timeScale = isEditMode ? timeScaleEdit : timeScale;
            }
        }

        int currentTotalFrameInt = GetCurrentTotalFrameInt();

        if(isEditMode){
            // currentFrameInt shouldnt overshoot the total frame int
            if(currentFrameInt > currentTotalFrameInt){
                currentFrameInt %= currentTotalFrameInt;
            }

            // if we change to a new animation clip, have to force it to start all over
            if(string.IsNullOrWhiteSpace(editor_forceStartStateName) == false){
                animator.Play(editor_forceStartStateName, 0, 0);
                editor_forceStartStateName = "";
                currentFrameInt = 0;
            }
            else { // the same animation so constantly play the frame we are at
                string currStateName = GetCurrentState(GetAllStates()).name;
                animator.Play(currStateName, 0, GetCurrentNormalizedTimeFromFrame());
            }
        }
        else { // not edit mode so constantly update frame from the animation
            // this works because the animator will be playing the animation anyway without
            // any interference from us, so we're jst getting the updated frame number
            currentFrame = GetCurrentAnimationFrame();
            currentFrameInt = (int) currentFrame;
        }

        UpdateEvents(); // To see if Event has to be executed

#if UNITY_EDITOR
        // apply changes in transform made to the object itself
        UpdateOffsetsFromCreatedObject_EditorOnly();
#endif 
    }

    private void OnValidate() {
        if(bundles != null && bundles.Count > 0){
            foreach (var bundle in bundles){
                if(bundle.spawnFrame < 0){
                    bundle.spawnFrame = 0;
                }
            }
        }

        // add to not make current frame negative? idk where this is taken care of

#if UNITY_EDITOR
        if(Application.isPlaying == false){
            currentFrameInt = 0;
        }
#endif
    }

    // Update Methods
    private void UpdateEvents(){
        if(bundles == null || bundles.Count == 0) return;

        AnimationClip currentClip = GetCurrentAnimationClip();

        foreach(var bundle in bundles){
            if(bundle.enabled == false) continue;

            if(bundle.animationClip != currentClip) continue;

            if(currentFrameInt < bundle.spawnFrame){
                bundle.isPlayed = false;
            }
            if(!bundle.isPlayed && currentFrameInt >= bundle.spawnFrame){
                bundle.isPlayed = true;
                SpawnObject(bundle);
                InvokeFunctions(bundle);
            }
        }
    }

    private void UpdateOffsetsFromCreatedObject_EditorOnly(){
        if(editor_enabled) return;

        foreach (var bundle in bundles){
            ModifyBundleTransformInfo(bundle);
        }
    }

    // Validation Methods
    private bool AnimatorIsValid(){
        return
            animator != null &&
            animator.enabled &&
            AllAnimationClips.Length > 0;
    }

    private bool AnimatorIsNotValid(){
        return
            animator == null ||
            !animator.enabled ||
            AllAnimationClips.Length == 0;
    }

    // Getter Methods
    private AnimationClip GetCurrentAnimationClip(){
        if(AnimatorIsNotValid()) return null;
        if(Application.isPlaying == false) return null;

        AnimatorClipInfo[] clipInfoArr = animator.GetCurrentAnimatorClipInfo(0);
        if(clipInfoArr.Length == 0) return null;

        return clipInfoArr[0].clip;
    }

    private float GetCurrentAnimationFrame(){
        if(AnimatorIsNotValid()) return 0f;
    
        AnimatorClipInfo[] clipInforArr = animator.GetCurrentAnimatorClipInfo(0);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        AnimationClip clip = clipInforArr[0].clip;

        // stateInfo.normalizedTime = integer part is the number of times a state has looped. The fraction par tis the % (0-1) of progress in the current loop
        float normTime = clip.isLooping ? stateInfo.normalizedTime % 1f : Mathf.Clamp01(stateInfo.normalizedTime);

        float currentFrame = normTime * clip.frameRate * clip.length;

        return currentFrame;
    }

    private float GetCurrentNormalizedTimeFromFrame(){
        if(AnimatorIsNotValid()) return 0f;

        AnimatorClipInfo[] clipInfoArr = animator.GetCurrentAnimatorClipInfo(0);
        if(clipInfoArr == null || clipInfoArr.Length == 0){
            return 0f;
        }

        AnimationClip clip = clipInfoArr[0].clip;
        return currentFrameInt / (clip.frameRate * clip.length);
    }

    private int GetTotalFrameInt(AnimationClip clip){
        if(AnimatorIsNotValid()) return 1;

        return (int) (clip.frameRate * clip.length);
    }

    private int GetCurrentTotalFrameInt(){
        AnimationClip currentClip = GetCurrentAnimationClip();
        if(currentClip == null) return 1;

        return GetTotalFrameInt(currentClip);
    }

    private AnimatorState[] GetAllStates(){
        if(AnimatorIsNotValid()) return null;

        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        return controller == null ? null : controller.layers.SelectMany(layer => layer.stateMachine.states).Select(state => state.state).ToArray();
    }

    private AnimatorState GetCurrentState(AnimatorState[] allStates){
        if(AnimatorIsNotValid()) return null;
        if(Application.isPlaying == false) return null;
        if(allStates == null || allStates.Length == 0) return null;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

        foreach (var state in allStates){
            if(stateInfo.IsName(state.name)){
                return state;
            }
        }
        return null;
    }

    // Private Methods
    private void SpawnObject(EventBundle bundle){
        if(bundle.prefab == null) return;

        if(bundle.spawnedObject != null){
            Destroy(bundle.spawnedObject.gameObject);
        }

#if UNITY_EDITOR
        if(isEditMode){
            editor_waitTime = 0;
        }
#endif

        Transform bundleTransform = Instantiate(bundle.prefab).transform;

        if(bundle.bone){
            bundleTransform.SetParent(bundle.bone);
        }

        bundleTransform.gameObject.SetActive(true);

        // if no parent, this is the same as transform.position
        bundleTransform.localPosition = bundle.position;
        bundleTransform.localEulerAngles = bundle.rotation;
        bundleTransform.localScale = bundle.scale;

        bundle.spawnedObject = bundleTransform;

        if(bundle.keepParentState == false && bundle.bone != null){
            bundle.spawnedObject.SetParent(null);
        }

#if UNITY_EDITOR_OPTION
        bundle.spawnedObject.gameObject.AddComponent<SpawnedAnimatorEventObject>();
#endif
    }

    private void InvokeFunctions(EventBundle bundle){
        if(bundle.eventFunctions == null) return;
    
        bundle.eventFunctions.Invoke();
    }

    private void ModifyBundleTransformInfo(EventBundle bundle){
        if(bundle.spawnedObject == null) return;

        if(bundle.keepParentState == false && bundle.bone != null){ // global
            // check on this code
            bundle.position = bundle.spawnedObject.position;
            bundle.rotation = bundle.spawnedObject.eulerAngles;
            bundle.scale = bundle.spawnedObject.lossyScale;
        }
        else{ // local to parent
            bundle.position = bundle.spawnedObject.localPosition;
            bundle.rotation = bundle.spawnedObject.localEulerAngles;
            bundle.scale = bundle.spawnedObject.localScale;
        }
    }

    // Save Playmode Changes Methods
#if UNITY_EDITOR
    // static constructors are used to initilaize any static data, or to perform a particular action that needs to be performed only once.
    // static constructor to be called only once before the first instance is created or any static members are referenced
    static CharacterEffectTool()
    {
        // Adding an0 event handler to an event listener
        EditorApplication.playModeStateChanged -= PlayModeStateChanged;
        EditorApplication.playModeStateChanged += PlayModeStateChanged;
    }

    private static void PlayModeStateChanged(PlayModeStateChange obj){
        switch(obj){
            case PlayModeStateChange.ExitingPlayMode:
                SaveAllDataToJson();
                break;
            case PlayModeStateChange.EnteredEditMode:
                LoadAllDataFromJson();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); // mark scene as modified
                EditorSceneManager.SaveOpenScenes(); // save all open scenes
                break;
        } 
    }

    private static string GetProjectPath(){
        string path = Application.dataPath; // C:/Users/cck/Documents/unity/CharacterTool/Assets
        return path.Substring(0, path.LastIndexOf('/')); // C:/Users/cck/Documents/unity/CharacterTool
    }

    private static string GetJsonFilePath(string fileName){ // makes a seralized json filepath using the filename given
        return $"{Path.Combine(GetProjectPath(), $"{fileName}.dat")}";
    }

    private static void SaveAllDataToJson(){
        var targets = FindObjectsOfType<CharacterEffectTool>();

        foreach (var target in targets){
            if(target.bundles != null){
                string jsonStr = JsonUtility.ToJson(new SerializableCharacterEffectTool(target));
                string filePath = GetJsonFilePath(target.GetInstanceID().ToString());
                File.WriteAllText(filePath, jsonStr, System.Text.Encoding.UTF8);
            }
        }
    }

    private static void LoadAllDataFromJson(){
        var targets = FindObjectsOfType<CharacterEffectTool>();

        foreach (var target in targets){
            if(target.bundles != null){
                string filePath = GetJsonFilePath(target.GetInstanceID().ToString());

                if(File.Exists(filePath)){
                    string jsonStr = File.ReadAllText(filePath);
                    SerializableCharacterEffectTool loaded = JsonUtility.FromJson<SerializableCharacterEffectTool>(jsonStr);
                    loaded.LoadValues(target);

                    for(int i = 0; i < target.bundles.Count; i++){
                        target.bundles[i].spawnedObject = null;
                    }

                    File.Delete(filePath);
                }
            }
        }
    }

    [Serializable] // The serialable version of CharacterEffectTool for JSON serialization storage of data (fields are all public)
    private class SerializableCharacterEffectTool {
        public bool isEditMode;
        public float timeScale;
        public float timeScaleEdit;
        public List<EventBundle> bundles;

        public SerializableCharacterEffectTool(CharacterEffectTool tool){
            this.isEditMode = tool.isEditMode;
            this.timeScale = tool.timeScale;
            this.timeScaleEdit = tool.timeScaleEdit;
            this.bundles = tool.bundles;
        }

        public void LoadValues(CharacterEffectTool tool){
            tool.isEditMode = this.isEditMode;
            tool.timeScale = this.timeScale;
            tool.timeScaleEdit = this.timeScaleEdit;
            tool.bundles = this.bundles;
        }
    }
#endif

#if UNITY_EDITOR_OPTION
    private class SpawnedAnimatorEventObject : MonoBehaviour { } 
#endif

    [Serializable]
    public class EventBundle {
        [HideInInspector] public bool enabled;
        [HideInInspector] public string name;
        [HideInInspector] public int spawnFrame;

        [HideInInspector] public AnimationClip animationClip;
        [HideInInspector] public int animationClipIndex;

        [HideInInspector] public GameObject prefab;
        [HideInInspector] public Transform bone;
        [HideInInspector] public bool keepParentState;
        [HideInInspector] public Transform spawnedObject;

        public UnityEvent eventFunctions;

        [HideInInspector] public Vector3 position;
        [HideInInspector] public Vector3 rotation;
        [HideInInspector] public Vector3 scale;

        [NonSerialized] public bool isPlayed = false;
        [HideInInspector] public bool edt_bundleFoldout = true;
        [HideInInspector] public bool edt_advancedFoldout = false;
        
        [HideInInspector] public bool prefabSelected = true;

        public EventBundle(){
            position = Vector3.zero;
            rotation = Vector3.zero;
            scale = Vector3.one;
            keepParentState = true;
            enabled = true;
        }

        public void DestroySpawnedObject(){
            if(spawnedObject != null){
                Destroy(spawnedObject.gameObject);
                spawnedObject = null;
            }
        }
    }

#if UNITY_EDITOR
    private const int EDITOR_WAITTIME_MAX = 1;
    private int editor_waitTime = EDITOR_WAITTIME_MAX;
    private string editor_forceStartStateName = "";
    private bool editor_enabled;
    private int editor_bundlesArrayCount = 0;

    [CustomEditor(typeof(CharacterEffectTool))]
    private class CharacterEffectToolEditor : Editor
    {
        private CharacterEffectTool tool;

        private bool isEnglish = true;
        private const string ENGLISH_KEY = "isEnglish";

        private bool bundleArrayFoldout = false;
        private const string ARRAY_FOLDOUT_KEY = "bundleArrayFoldout";

        private bool animationEventFoldout = false;
        private const string ANIMATION_EVENT_FOLDOUT_KEY = "animationEventFoldout";

        private int totalFrameInt;

        private AnimationClip currentClip;
        private AnimationClip[] allClips;
        private string[] allClipStrings;

        private AnimatorState currentState;
        private AnimatorState[] allStates;
        private string[] allStateNames;

        private Transform copyLocalTransform;
        private Transform copyGlobalTransform;

        private Color ORIG_COLOR;
        private Color ORIG_BG_COLOR;
        private Color TURQUOISE;
        private Color PINK;
        private Color BLUE;
        private Color GREEN;
        private Color RED;
        private Color LIGHT_BLUE;
        private Color YELLOW;

        private void OnEnable() {
            tool = target as CharacterEffectTool;

            if(tool.animator == null) tool.animator = tool.GetComponent<Animator>();

            timeController = tool; // still don't know why we do this

            isEnglish = EditorPrefs.GetBool(ENGLISH_KEY, true);
            bundleArrayFoldout = EditorPrefs.GetBool(ARRAY_FOLDOUT_KEY, false);
            animationEventFoldout = EditorPrefs.GetBool(ANIMATION_EVENT_FOLDOUT_KEY, false);

            tool.editor_enabled = true;

            ORIG_COLOR = GUI.color;
            ORIG_BG_COLOR = GUI.backgroundColor;
            TURQUOISE = new Color32(85, 239, 196, 255);
            BLUE = new Color32(9, 132, 227, 255);
            PINK = new Color32(253, 121, 168, 255);
            GREEN = new Color32(0, 148, 50, 255);
            RED = new Color32(234, 32, 39, 255);
            LIGHT_BLUE = new Color32(116, 185, 255, 255);
            YELLOW = new Color32(253, 203, 110, 255);
        }

        private void OnDisable() {
            tool.editor_enabled = false;
        }

        public override void OnInspectorGUI(){
            DrawLanguageButton();

            if(tool.animator == null){
                EditorGUILayout.HelpBox(
                    EngHan("Animator component missing.", "Animator 컴포넌트가 존재하지 않습니다."),
                    MessageType.Error
                );
            }
            else if(tool.animator.enabled == false){
                EditorGUILayout.HelpBox(
                    EngHan("Animator component is disabled.", "Animator 컴포넌트가 비활성화 되있습니다."),
                    MessageType.Warning
                );
            }
            else if(tool.animator.runtimeAnimatorController == null){
                EditorGUILayout.HelpBox(
                    EngHan("Runtime animator controller is undefined", "Animator 컴포넌트에 Controller이 등록되지 않았습니다."),
                    MessageType.Warning
                );
            }
            else if(tool.AllAnimationClips.Length == 0){
                EditorGUILayout.HelpBox(
                    EngHan("Animator has no animation clips regsitered.", "Animator에 애니메이션 클립이 없습니다."),
                    MessageType.Warning
                );
            }
            else{
                DrawInspectorGui();
            }
        }

        private void DrawLanguageButton(){
            Space(0f);
            float screenWidth = Screen.width;
            float buttonWidth = 80f;
            float buttonHeight = 20f;
            float buttonX = screenWidth - buttonWidth - 5f;
            float buttonY = 5f;

            if(GUI.Button(new Rect(buttonX, buttonY, buttonWidth, buttonHeight), "Eng / 한글")){
                isEnglish = !isEnglish;
                EditorPrefs.SetBool(ENGLISH_KEY, isEnglish);
            }
        }

        private void DrawInspectorGui(){
            totalFrameInt = tool.GetCurrentTotalFrameInt();

            allStates = tool.GetAllStates();
            allStateNames = allStates.Select(state => state.name).ToArray();
            currentState = tool.GetCurrentState(allStates);

            allClips = tool.AllAnimationClips.Distinct().ToArray();
            allClipStrings = allClips.Select(clips => clips.name).ToArray();
            currentClip = tool.GetCurrentAnimationClip();

            Undo.RecordObject(tool, "Character Effect Tool");
            
            DrawMainControl();

            Space(8f);

            DrawEffectsList();

            Space(8f);

            DrawAllAnimationList();
        }

        private void DrawMainControl(){
            tool.isEditMode = EditorGUILayout.Toggle(EngHan("Edit Mode", "편집 모드"), tool.isEditMode);

            Space(5f);

            if(!tool.isEditMode){
                GUI.color = TURQUOISE * 2.0f;
            }
            tool.timeScale = EditorGUILayout.Slider(EngHan("Time Scale(Play Mode)", "게임 진행 속도(재생 모드)"), tool.timeScale, 0f, 1f);
            GUI.color = ORIG_COLOR; // reset to original color

            if(tool.isEditMode){
                GUI.color = TURQUOISE * 2.0f;
            }
            tool.timeScaleEdit = EditorGUILayout.Slider(EngHan("Time Scale(Edit Mode)", "게임 진행 속도(편집 모드)"), tool.timeScaleEdit, 0f, 1f);
            GUI.color = ORIG_COLOR;

            if(Application.isPlaying){
                Space(8f);

                GUI.color = PINK * 2.0f;

                // Animator State Box
                if(allStates != null && allStates.Length > 1){
                    string currClipCaption = EngHan("Animator State", "애니메이션");

                    if(!tool.isEditMode){
                        EditorGUI.BeginDisabledGroup(true);
                        EditorGUILayout.TextField(currClipCaption, currentState.name);
                        EditorGUI.EndDisabledGroup();
                    }
                    else{
                        int index = 0;
                        for(int i = 0; i < allStates.Length; i++){
                            if(allStates[i] == currentState){
                                index = i;
                                break;
                            }
                        }

                        EditorGUI.BeginChangeCheck();
                        index = EditorGUILayout.Popup(currClipCaption, index, allStateNames);
                        if(EditorGUI.EndChangeCheck()){
                            tool.editor_forceStartStateName = allStateNames[index];
                        }
                    }
                }

                // Current Frame Section
                EditorGUI.BeginDisabledGroup(!tool.isEditMode);
                tool.currentFrameInt = EditorGUILayout.IntSlider(EngHan("Current Frame", "현재 프레임"), tool.currentFrameInt, 0, totalFrameInt);
                EditorGUI.EndDisabledGroup();
                

                if(tool.isEditMode){
                    Space(2f);
                    EditorGUILayout.BeginHorizontal();

                    if(GUILayout.Button("<<")) tool.currentFrameInt -= 2;
                    if(GUILayout.Button("<")) tool.currentFrameInt--;
                    if(GUILayout.Button(">")) tool.currentFrameInt++;
                    if(GUILayout.Button(">>")) tool.currentFrameInt += 2;

                    EditorGUILayout.EndHorizontal();
                }
                GUI.color = ORIG_COLOR;

                Space(8f);

                if(GUILayout.Button(EngHan("Restart From Beginning", "처음부터 다시 재생"))){
                    RemoveAllCreatedObjects();
                    tool.currentFrameInt = 0;
                    tool.animator.Play(0,0,0);
                }
                Space(1f);
                if(GUILayout.Button(EngHan("Remove All Spawned Objects", "생성된 모든 오브젝트 제거"))){
                    RemoveAllCreatedObjects();
                }
            
            }
        }

        private void RemoveAllCreatedObjects(){
            if(tool.bundles != null && tool.bundles.Count > 0){
                for(int i = 0; i < tool.bundles.Count; i++){
                    tool.bundles[i].DestroySpawnedObject();
                }
            }
#if UNITY_EDITOR_OPTION
            var spawns = FindObjectsOfType<SpawnedAnimatorEventObject>();
            if(spawns != null && spawns.Length > 0){
                for(int i = 0; i < spawns.Length; i++){
                    Destroy(spawns[i].gameObject);
                }
            }
#endif
        }

        private void DrawEffectsList(){
            bool oldFoldout = bundleArrayFoldout;
            bundleArrayFoldout = EditorGUILayout.Foldout(bundleArrayFoldout, EngHan("Events", "이벤트 목록"), true);

            if(oldFoldout != bundleArrayFoldout){
                EditorPrefs.SetBool(ARRAY_FOLDOUT_KEY, bundleArrayFoldout);
            }

            if(bundleArrayFoldout){
                EditorGUILayout.BeginHorizontal();
                
                EditorGUI.BeginChangeCheck();
                tool.editor_bundlesArrayCount = EditorGUILayout.IntField(EngHan("Number of Events", "개수"), tool.bundles.Count);
                if(EditorGUI.EndChangeCheck()){
                    if(tool.editor_bundlesArrayCount < 0) tool.editor_bundlesArrayCount = 0;

                    ref int newSize = ref tool.editor_bundlesArrayCount;
                    int oldSize = tool.bundles.Count;

                    if(newSize < oldSize){
                        for(int i = oldSize - 1; i >= newSize; i--){
                            tool.bundles.RemoveAt(i);
                        }
                    }
                    else if(newSize > oldSize){
                        for(int i = oldSize; i < newSize; i++){
                            tool.bundles.Add(new EventBundle());
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                Space(3f);

                // Add, Minus Buttons
                EditorGUILayout.BeginHorizontal();
                    GUI.backgroundColor = GREEN * 2.0f;
                    bool addEvent = GUILayout.Button("+");
                    if(addEvent){
                        tool.bundles.Add(new EventBundle());
                    }

                    GUI.backgroundColor = RED * 2.0f;
                    bool removeEvent = GUILayout.Button("-");
                    if(removeEvent){
                        tool.bundles.RemoveAt(tool.bundles.Count - 1);
                    }

                    GUI.backgroundColor = ORIG_BG_COLOR;
                EditorGUILayout.EndHorizontal();

                for(int i = 0; i < tool.bundles.Count; i++){
                    DrawEventBundle(tool.bundles[i], i);
                }

                if(tool.bundles.Count > 0){
                    Space(8f);

                    GUI.backgroundColor = GREEN * 2.0f;
                    if(GUILayout.Button("+")){
                        tool.bundles.Add(new EventBundle());
                    }
                    GUI.backgroundColor = ORIG_BG_COLOR;
                }
            }
        }

        private void DrawEventBundle(EventBundle bundle, int index){
            Space(8f);

            string name = string.IsNullOrWhiteSpace(bundle.name) ? EngHan($"Event {index}", $"이벤트 {index}") : bundle.name;

            EditorGUILayout.BeginHorizontal();
                float foldoutLeftMargin = 12;
                GUI.color = Color.clear;
                EditorGUILayout.LabelField(" ", GUILayout.Width(foldoutLeftMargin));

                if(bundle.enabled){
                    GUI.color = bundle.prefab == null && bundle.eventFunctions.GetPersistentEventCount() <= 0 ? RED *3f : TURQUOISE * 2.0f;
                }
                else {
                    GUI.color = Color.gray;
                }

                bundle.edt_bundleFoldout = EditorGUILayout.Foldout(bundle.edt_bundleFoldout, name, true);

                GUI.color = ORIG_COLOR;

                // enabled checkbox
                Rect foldoutRect = GUILayoutUtility.GetLastRect();
                float buttonHeight = 20f;
                float buttonWidth = 20f;
                float Y = foldoutRect.yMin;
                float X = foldoutRect.x - 28f;

                bundle.enabled = EditorGUI.Toggle(new Rect(X, Y, buttonWidth, buttonHeight), bundle.enabled);
                if(bundle.enabled == false){
                    bundle.DestroySpawnedObject();
                }

                EditorGUI.BeginDisabledGroup(index >= tool.bundles.Count - 1);
                    if(GUILayout.Button("▼", GUILayout.Width(22f), GUILayout.Height(16f))){
                        EventBundle temp = tool.bundles[index];
                        tool.bundles[index] = tool.bundles[index + 1];
                        tool.bundles[index + 1] = temp;
                    }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(index <= 0);
                    if(GUILayout.Button("▲", GUILayout.Width(22f), GUILayout.Height(16f))){
                        EventBundle temp = tool.bundles[index];
                        tool.bundles[index] = tool.bundles[index - 1];
                        tool.bundles[index - 1] = temp;
                    }
                EditorGUI.EndDisabledGroup();

                // Spacing Horizontally
                EditorGUILayout.LabelField(" ", GUILayout.Width(8f));

                GUI.backgroundColor = RED * 2.0f;

                if(GUILayout.Button("-", GUILayout.Width(35f), GUILayout.Height(16f))){
                    tool.bundles.RemoveAt(index);
                }

                GUI.backgroundColor = ORIG_BG_COLOR;
            EditorGUILayout.EndHorizontal();

            if(bundle.edt_bundleFoldout){
                EditorGUI.indentLevel += 2;
                
                EditorGUI.BeginDisabledGroup(bundle.enabled == false);
                    // Name
                    bundle.name = EditorGUILayout.TextField(EngHan("Name", "이름"), bundle.name);

                    // Animation Clip Dropdown
                    if(allClips?.Length > 0){ // the ? is a null-conditional operator where if allClips is null, instead of null-point error, it will jst return null
                        if(allClips.Length >= 2){
                            bundle.animationClipIndex = EditorGUILayout.Popup(EngHan("Animation Clip", "애니메이션"), bundle.animationClipIndex, allClipStrings);
                        }
                        else{
                            EditorGUI.BeginDisabledGroup(true);
                                EditorGUILayout.TextField(EngHan("Animation Clip", "애니메이션"), allClipStrings[bundle.animationClipIndex]);
                            EditorGUI.EndDisabledGroup();
                        }

                        if(bundle.animationClipIndex < 0 || bundle.animationClipIndex >= allClips.Length){
                            bundle.animationClipIndex = 0;
                        }
                        bundle.animationClip = allClips[bundle.animationClipIndex];

                        if(Application.isPlaying && tool.isEditMode && currentClip != bundle.animationClip){
                            string diffClipWarning = EngHan("Event's animation does not match currently playing animation.", "현재 재생 중인 애니메이션과 일치하지 않습니다.");
                            EditorGUILayout.HelpBox(diffClipWarning, MessageType.Warning);
                            Space(4f);
                        }

                         // Spawn Frame
                        if(currentClip == bundle.animationClip && tool.currentFrameInt == bundle.spawnFrame){
                            GUI.color = TURQUOISE * 2.0f;
                        }

                        int maxFrame = tool.GetTotalFrameInt(bundle.animationClip) - 1;
                        if(maxFrame < 0) maxFrame = 0;

                        bundle.spawnFrame = EditorGUILayout.IntSlider(EngHan("Spawn Frame", "생성 프레임"), bundle.spawnFrame, 1, maxFrame);

                        GUI.color = ORIG_COLOR;
                    }
                    else {
                        bundle.animationClip = null;
                        bundle.animationClipIndex = 0;
                        bundle.spawnFrame = 0;
                    }

                    Space(2f);

                    // Jump to Spawn and Set Current Frame buttons
                    if(Application.isPlaying && tool.isEditMode){
                        EditorGUI.BeginDisabledGroup(currentClip != bundle.animationClip || tool.currentFrameInt == bundle.spawnFrame);
                            EditorGUILayout.BeginHorizontal();

                                EditorGUILayout.LabelField(" ", GUILayout.Width(28f));

                                GUI.backgroundColor = BLUE * 2.0f;
                                if(currentClip == bundle.animationClip && tool.currentFrameInt == bundle.spawnFrame) GUI.backgroundColor = ORIG_BG_COLOR;

                                if(GUILayout.Button(EngHan("Jump to Spawn Frame", "생성 프레임으로 이동"))){
                                    tool.currentFrameInt = bundle.spawnFrame;
                                }

                                // GUI.backgroundColor = isSame ? ORIG_BG_COLOR : BLUE * 2.0f;
                                if(GUILayout.Button(EngHan("Set Spawn Frame", "생성 프레임 지정"))){
                                    bundle.spawnFrame = tool.currentFrameInt;
                                }

                                GUI.backgroundColor = ORIG_BG_COLOR;
                            EditorGUILayout.EndHorizontal();
                        EditorGUI.EndDisabledGroup();
                    }

                    EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(" ", GUILayout.Width(28f));

                        GUI.backgroundColor = bundle.prefabSelected ? LIGHT_BLUE * 2.0f : ORIG_BG_COLOR;
                        if(GUILayout.Button(EngHan("Prefab ", "프리팹"))){
                            bundle.prefabSelected = true;
                        }

                        GUI.backgroundColor = bundle.prefabSelected ? ORIG_BG_COLOR : LIGHT_BLUE * 2.0f;
                        if(GUILayout.Button(EngHan("Functions", "함수"))){
                            bundle.prefabSelected = false;
                        }

                        GUI.backgroundColor = ORIG_BG_COLOR;
                    EditorGUILayout.EndHorizontal();

                    // Warning for only being able to be edited during edit mode
                    if(Application.isPlaying && !tool.isEditMode){
                        EditorGUILayout.HelpBox(EngHan("Can only be edited in edit mode", "편집 모드에서만 수정할 수 있습니다."), MessageType.Warning);
                        Space(4f);
                    }

                    Space(2f);

                    // Prefab Section
                    if(bundle.prefabSelected){
                        EditorGUI.BeginDisabledGroup(Application.isPlaying && !tool.isEditMode);
                            // Event Prefab
                            GameObject prevPrefab = bundle.prefab;
                            EditorGUI.BeginChangeCheck();
                                if(bundle.enabled == false){
                                    GUI.color = Color.gray;
                                }
                                else{
                                    GUI.color = bundle.prefab == null ? RED * 2.0f : TURQUOISE * 2.0f;
                                }

                                bundle.prefab = EditorGUILayout.ObjectField(EngHan("Prefab Object", "프리팹 오브젝트"), bundle.prefab, typeof(GameObject), true) as GameObject;
                                GUI.color = ORIG_COLOR;

                            if(EditorGUI.EndChangeCheck()){
                                // Name Handling with prefab change
                                if(bundle.prefab == null){
                                    bundle.name = "";
                                }
                                else{
                                    bundle.name = bundle.prefab.name;
                                    if(prevPrefab == null){

                                        // First save the new prefab's global transform data to bundle
                                        bundle.position = bundle.prefab.transform.position;
                                        bundle.rotation = bundle.prefab.transform.eulerAngles;
                                        bundle.scale = bundle.prefab.transform.lossyScale;

                                        // Second if parent bone exists, change to parent's local
                                        if(bundle.bone != null){
                                            Matrix4x4 mat = bundle.bone.worldToLocalMatrix;

                                            bundle.position = mat.MultiplyPoint(bundle.position);
                                            bundle.rotation = mat.MultiplyVector(bundle.rotation);
                                            bundle.scale = mat.MultiplyVector(bundle.scale);
                                        }
                                    }
                                }
                            }

                            // Backup previous data
                            Transform prevBone = bundle.bone;
                            EditorGUILayout.BeginHorizontal();
                                EditorGUI.BeginChangeCheck();
                                    bundle.bone = EditorGUILayout.ObjectField(EngHan("Bone", "뼈"), bundle.bone, typeof(Transform), true) as Transform;
                                if(EditorGUI.EndChangeCheck()){
                                    if(bundle.bone != null){
                                        if(PrefabUtility.IsPartOfPrefabAsset(bundle.bone)){
                                            string boneWarningMsg = EngHan("Cannot register a prefab asset as parent bone.", "프리팹 에셋은 부모 트랜스폼으로 설정될 수 없습니다.");
                                            bundle.bone = prevBone;
                                            EditorUtility.DisplayDialog(
                                                EngHan("Error", "에러"), boneWarningMsg, EngHan("Okay", "네"));
                                        }
                                        else{
                                            bool flag = false;
                                            for(int i = 0; i < tool.bundles.Count; i++){
                                                if(bundle.bone == tool.bundles[i].spawnedObject){
                                                    flag = true;
                                                    break;
                                                }
                                            }

                                            if(flag){
                                                bundle.bone = prevBone;
                                                EditorUtility.DisplayDialog(
                                                    EngHan("Error", "에러"),
                                                    EngHan("Cannot register a spawned object as parent bone.", "생성된 오브젝트를 부모 트랜스폼으로 설정할 수 없습니다."),
                                                    EngHan("Okay", "네")
                                                );
                                            }
                                        }
                                    }

                                    if(prevBone == null && bundle.bone != null){
                                        bundle.keepParentState = true;
                                    }

                                    ApplyTransformChangesToSpawnedObject();
                                }

                                // Delete bone button
                                if(bundle.bone != null){
                                    GUI.backgroundColor = RED * 2.0f;

                                    if(GUILayout.Button("X", GUILayout.Width(24f))){
                                        bundle.bone = null;
                                        ApplyTransformChangesToSpawnedObject();
                                    }

                                    GUI.backgroundColor = ORIG_BG_COLOR;
                                }

                            EditorGUILayout.EndHorizontal(); // Bone

                            // Keep Parent State Checkbox
                            if(bundle.bone == null){
                                bundle.keepParentState = false;
                            }
                            else {
                                EditorGUI.BeginChangeCheck();
                                    bundle.keepParentState = EditorGUILayout.Toggle(EngHan("Keep Parent State", "부모-자식 관계 유지"), bundle.keepParentState);
                                if(EditorGUI.EndChangeCheck()){
                                    ApplyTransformChangesToSpawnedObject();
                                }
                            }
                        EditorGUI.EndDisabledGroup(); // Application.isPlaying && !tool.isEditMode
                        
                        void ApplyTransformChangesToSpawnedObject(){
                            if(bundle.spawnedObject != null){
                                // if bone exists
                                if(bundle.bone != null){
                                    if(bundle.keepParentState){
                                        bundle.spawnedObject.SetParent(bundle.bone);

                                        bundle.position = bundle.spawnedObject.localPosition;
                                        bundle.rotation = bundle.spawnedObject.localEulerAngles;
                                        bundle.scale = bundle.spawnedObject.localScale;
                                    }
                                    else {
                                        bundle.position = bundle.spawnedObject.localPosition;
                                        bundle.rotation = bundle.spawnedObject.localEulerAngles;
                                        bundle.scale = bundle.spawnedObject.localScale;

                                        bundle.spawnedObject.SetParent(null);
                                    }
                                }
                                else{
                                    bundle.spawnedObject.SetParent(null);

                                    bundle.position = bundle.spawnedObject.position;
                                    bundle.rotation = bundle.spawnedObject.eulerAngles;
                                    bundle.scale = bundle.spawnedObject.lossyScale;
                                }
                            }
                            else {
                                Matrix4x4 mat = default;
                                bool transformFlag = false;

                                // Removed Bone
                                if(prevBone != null && bundle.bone == null){
                                    mat = prevBone.localToWorldMatrix;
                                    transformFlag = true;
                                }
                                // No bone before but new bone added
                                else if(prevBone == null && bundle.bone != null){
                                    mat = bundle.bone.worldToLocalMatrix;
                                    transformFlag = true;
                                }
                                // yes bone and new bone
                                else if(prevBone != null && bundle.bone != null){
                                    mat = bundle.bone.worldToLocalMatrix * prevBone.localToWorldMatrix;
                                    transformFlag = true;
                                }

                                if(transformFlag){
                                    bundle.position = mat.MultiplyPoint(bundle.position);
                                    bundle.rotation = mat.MultiplyVector(bundle.rotation);
                                    bundle.scale = mat.MultiplyVector(bundle.scale);
                                }
                            }
                        }

                        // Spawned Object
                        if(Application.isPlaying){
                            if(bundle.spawnedObject != null){
                                GUI.color = TURQUOISE * 2.0f;
                            }

                            EditorGUI.BeginDisabledGroup(true);
                                EditorGUILayout.ObjectField(EngHan("Spawned Object", "생성된 오브젝트"), bundle.spawnedObject, typeof(GameObject), true);
                            EditorGUI.EndDisabledGroup();

                            GUI.color = ORIG_COLOR;
                        }

                        Space(2f);

                        // Spawn Destroy Button
                        if(Application.isPlaying && tool.isEditMode){
                            EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField(" ", GUILayout.Width(28f));

                                EditorGUI.BeginDisabledGroup(currentClip != bundle.animationClip || tool.currentFrameInt != bundle.spawnFrame || bundle.prefab == null);
                                    if(GUILayout.Button(EngHan("Spawn Object", "오브젝트 생성"))){
                                        tool.SpawnObject(bundle);
                                    }
                                EditorGUI.EndDisabledGroup();

                                EditorGUI.BeginDisabledGroup(bundle.spawnedObject == null);
                                    if(GUILayout.Button(EngHan("Destroy Object", "오브젝트 제거"))){
                                        bundle.DestroySpawnedObject();
                                    }
                                EditorGUI.EndDisabledGroup();
                            EditorGUILayout.EndHorizontal();
                        }

                        Space(8f);

                        bool axisExistAndKeepParentTrue = 
                            bundle.bone == null || 
                            bundle.keepParentState == true || 
                            (tool.currentFrameInt == bundle.spawnFrame && currentClip == bundle.animationClip);
                        
                        EditorGUI.BeginDisabledGroup(!axisExistAndKeepParentTrue);
                            EditorGUI.BeginChangeCheck();
                                bool isLocalState = bundle.bone != null;
                                string posStr = isLocalState ? EngHan("Local Position", "로컬 위치") : EngHan("Global Position", "월드 위치");
                                string rotStr = isLocalState ? EngHan("Local Rotation", "로컬 회전") : EngHan("Global Rotation", "월드 회전");
                                string scaleStr = isLocalState ? EngHan("Local Scale", "로컬 크기") : EngHan("Global Scale", "월드 크기");

                                EditorGUILayout.BeginHorizontal();
                                    bundle.position = EditorGUILayout.Vector3Field(posStr, bundle.position);

                                    GUI.backgroundColor = YELLOW * 1.5f;
                                    if(GUILayout.Button("R", GUILayout.Width(24f))){
                                        bundle.position = Vector3.zero;
                                    }
                                    GUI.backgroundColor = ORIG_BG_COLOR;
                                EditorGUILayout.EndHorizontal();

                                EditorGUILayout.BeginHorizontal();
                                    bundle.rotation = EditorGUILayout.Vector3Field(rotStr, bundle.rotation);

                                    GUI.backgroundColor = YELLOW * 1.5f;
                                    if(GUILayout.Button("R", GUILayout.Width(24f))){
                                        bundle.rotation = Vector3.zero;
                                    }
                                    GUI.backgroundColor = ORIG_BG_COLOR;
                                EditorGUILayout.EndHorizontal();

                                EditorGUILayout.BeginHorizontal();
                                    bundle.scale = EditorGUILayout.Vector3Field(scaleStr, bundle.scale);

                                    GUI.backgroundColor = YELLOW * 1.5f;
                                    if(GUILayout.Button("R", GUILayout.Width(24f))){
                                        bundle.scale = Vector3.one;
                                    }
                                    GUI.backgroundColor = ORIG_BG_COLOR;
                                EditorGUILayout.EndHorizontal();

                            // Apply all changes
                            if(EditorGUI.EndChangeCheck() && bundle.spawnedObject != null){
                                if(bundle.bone != null){
                                    if(bundle.keepParentState){
                                        bundle.spawnedObject.localPosition = bundle.position;
                                        bundle.spawnedObject.localEulerAngles = bundle.rotation;
                                        bundle.spawnedObject.localScale = bundle.scale;
                                    }
                                    else{
                                        Matrix4x4 mat = bundle.bone.localToWorldMatrix;

                                        bundle.spawnedObject.position = mat.MultiplyPoint(bundle.position);
                                        bundle.spawnedObject.eulerAngles = mat.MultiplyVector(bundle.rotation);
                                        bundle.spawnedObject.localScale = mat.MultiplyVector(bundle.scale);
                                    }
                                }
                                else {
                                    bundle.spawnedObject.localPosition = bundle.position;
                                    bundle.spawnedObject.localEulerAngles = bundle.rotation;
                                    bundle.spawnedObject.localScale = bundle.scale;
                                }
                            }

                            // Advanced Foldout
                            bundle.edt_advancedFoldout = EditorGUILayout.Foldout(bundle.edt_advancedFoldout, EngHan("Advanced Options", "고급 설정"), true);

                            if(bundle.edt_advancedFoldout){
                                EditorGUILayout.BeginHorizontal();
                                    EditorGUILayout.LabelField(" ", GUILayout.Width(28f));
                                    if(GUILayout.Button(EngHan("Reset Position, Rotation, Scale", "위치, 회전, 크기 초기화"))){
                                        bundle.position = Vector3.zero;
                                        bundle.rotation = Vector3.zero;
                                        bundle.scale = Vector3.one;

                                        if(bundle.spawnedObject != null){
                                            tool.SpawnObject(bundle);
                                        }
                                    }
                                EditorGUILayout.EndHorizontal();

                                Space(1f);

                                string localCopyStr = EngHan("Copy Local Transform", "로컬 트랜스폼 복제");
                                string globalCopyStr = EngHan("Copy Global Transform", "월드 트랜스폼 복제");

                                EditorGUI.BeginChangeCheck();
                                    copyLocalTransform = EditorGUILayout.ObjectField(localCopyStr, copyLocalTransform, typeof(Transform), true) as Transform;
                                if(EditorGUI.EndChangeCheck()){
                                    bundle.position = copyLocalTransform.localPosition;
                                    bundle.rotation = copyLocalTransform.localEulerAngles;
                                    bundle.scale = copyLocalTransform.localScale;

                                    copyLocalTransform = null;
                                }

                                EditorGUI.BeginChangeCheck();
                                    copyGlobalTransform = EditorGUILayout.ObjectField(globalCopyStr, copyGlobalTransform, typeof(Transform), true) as Transform;
                                if(EditorGUI.EndChangeCheck()){
                                    bundle.position = copyGlobalTransform.position;
                                    bundle.rotation = copyGlobalTransform.eulerAngles;
                                    bundle.scale = copyGlobalTransform.lossyScale;

                                    copyGlobalTransform = null;
                                }
                            }

                        EditorGUI.EndDisabledGroup(); // Axis != null Keep Parent == true;
                    }
                    else { // Functions selected
                        EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(" ", GUILayout.Width(28f));

                            serializedObject.Update();
                            SerializedProperty bndls = serializedObject.FindProperty("bundles");
                            SerializedProperty currentPartProperty = bndls.GetArrayElementAtIndex(index);
                            EditorGUILayout.PropertyField(currentPartProperty.FindPropertyRelative("eventFunctions"), new GUIContent(EngHan("Event Functions", "이벤트 함수들")));
                            serializedObject.ApplyModifiedProperties();
                        EditorGUILayout.EndHorizontal();

                    }
                EditorGUI.EndDisabledGroup(); // bundle.enabled

                EditorGUI.indentLevel -= 2;
            }
        }

        private void DrawAllAnimationList(){

        }

        private string EngHan(string eng, string han){
            return isEnglish? eng : han;
        }

        private void Space(float width)
        {
#if UNITY_2019_3_OR_NEWER
            EditorGUILayout.Space(width);
#else
            EditorGUILayout.Space();
#endif
        }

    } // Custom Editor Class End Bracket
#endif
}
