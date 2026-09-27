using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// EDITOR-ONLY tool. Must live in a folder named "Editor" (e.g. Assets/scripts/Editor/)
/// so Unity excludes it from game builds.
///
/// Builds an Animator Controller out of the animation clips baked into Assets/Mario64.fbx
/// (that FBX ships with the ENTIRE SM64 animation library as sub-assets - hundreds of clips),
/// wires up states/transitions that match the moves implemented in Movement.cs, and assigns
/// the controller + a Generic Avatar to the Animator on the "Armature" child of the "Mario64"
/// GameObject that's already in the open scene.
///
/// HOW TO RUN:
/// 1. Drop this file in Assets/scripts/Editor/ and let Unity compile.
/// 2. Make sure Assets/Mario64.fbx has been reimported with Avatar Setup =
///    "Create From This Model" (its .meta already sets this - just let Unity reimport it).
/// 3. Open the scene that has the "Mario64" GameObject in it (e.g. SampleScene).
/// 4. In the Unity menu bar: Tools > Player > Generate Mario64 Animator Controller.
/// 5. Check the Console for a success message, then press Play and move around - you should
///    see Idle/Walk/Run blending, and Jump/LongJump/Backflip/SideFlip/Dive/Rolling play
///    automatically because Movement.cs calls Animator.SetTrigger/SetBool/SetFloat for you.
///
/// NOTE ON CLIP NAMES: this animation pack doesn't use the move names you'd expect
/// ("Backflip", "LongJump", ...) - it uses the raw take names baked into the FBX. The closest
/// real match for each Movement.cs trigger is picked below; see the comment next to each one.
/// The one open question is Backflip: there's no clip in this pack literally named for a
/// stationary crouch-backflip, so it currently reuses the Double Jump clip ("Armature|Jump2")
/// as a stand-in. If you find/prefer a different clip for it, change BackflipClipName below.
/// </summary>
public static class PlayerAnimatorSetup
{
    private const string FbxPath = "Assets/Mario64.fbx";
    private const string ControllerPath = "Assets/PlayerAnimatorController.controller";
    private const string MarioObjectName = "Mario64";
    private const string ModelChildName = "Armature";

    private const string IdleClipName = "Armature|Wait";
    private const string WalkClipName = "Armature|Walk";
    private const string RunClipName = "Armature|Run";
    private const string JumpClipName = "Armature|Jump";
    private const string LandClipName = "Armature|Land";
    private const string LongJumpClipName = "Armature|JumpBroad";     // "broad jump" = long jump
    private const string DiveClipName = "Armature|Dive";
    private const string RollingClipName = "Armature|Rolling";
    private const string SideFlipClipName = "Armature|SpinJumpL";     // spin jump = side flip
    private const string BackflipClipName = "Armature|Jump2";         // stand-in, see note above

    [MenuItem("Tools/Player/Generate Mario64 Animator Controller")]
    public static void Generate()
    {
        Dictionary<string, AnimationClip> clips = LoadClips();

        AnimationClip idle = RequireClip(clips, IdleClipName);
        AnimationClip walk = RequireClip(clips, WalkClipName);
        AnimationClip run = clips.GetValueOrDefault(RunClipName);
        AnimationClip jump = RequireClip(clips, JumpClipName);
        AnimationClip land = clips.GetValueOrDefault(LandClipName);
        AnimationClip longJump = clips.GetValueOrDefault(LongJumpClipName);
        AnimationClip dive = clips.GetValueOrDefault(DiveClipName);
        AnimationClip rolling = clips.GetValueOrDefault(RollingClipName);
        AnimationClip sideFlip = clips.GetValueOrDefault(SideFlipClipName);
        AnimationClip backflip = clips.GetValueOrDefault(BackflipClipName);

        if (idle == null || walk == null || jump == null)
        {
            Debug.LogError("PlayerAnimatorSetup: couldn't find one or more required clips in " + FbxPath + ". Aborting.");
            return;
        }

        GameObject marioRoot = GameObject.Find(MarioObjectName);
        if (marioRoot == null)
        {
            Debug.LogError("PlayerAnimatorSetup: couldn't find a '" + MarioObjectName + "' GameObject in the open scene. " +
                            "Open the scene that has it (e.g. SampleScene) and run this again.");
            return;
        }

        Transform modelTransform = marioRoot.transform.Find(ModelChildName);
        if (modelTransform == null)
        {
            Debug.LogError("PlayerAnimatorSetup: couldn't find a '" + ModelChildName + "' child under " + MarioObjectName + ".");
            return;
        }

        Avatar avatar = LoadAvatar();
        if (avatar == null)
        {
            Debug.LogError("PlayerAnimatorSetup: no Avatar found on " + FbxPath + ". Select it in the Project window, " +
                            "open the Rig tab, set Animation Type to Generic and Avatar Definition to " +
                            "'Create From This Model', click Apply, then run this again.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("LongJump", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Backflip", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("SideFlip", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dive", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Rolling", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine rootSm = controller.layers[0].stateMachine;

        // --- Locomotion blend tree (Idle / Walk / Run) ---
        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree blendTree);
        blendTree.blendType = BlendTreeType.Simple1D;
        blendTree.blendParameter = "Speed";
        blendTree.AddChild(idle, 0f);
        blendTree.AddChild(walk, 0.5f);
        blendTree.AddChild(run != null ? run : walk, 1f);
        rootSm.defaultState = locomotion;

        // --- Jump -> Land -> Locomotion ---
        AnimatorState jumpState = AddState(rootSm, "Jump", jump, 450, -150);
        AddAnyStateTrigger(rootSm, jumpState, "Jump");

        if (land != null)
        {
            AnimatorState landState = AddState(rootSm, "Land", land, 650, -150);

            AnimatorStateTransition jumpToLand = jumpState.AddTransition(landState);
            jumpToLand.hasExitTime = false;
            jumpToLand.duration = 0.1f;
            jumpToLand.AddCondition(AnimatorConditionMode.If, 0, "Grounded");

            AnimatorStateTransition landToLoco = landState.AddTransition(locomotion);
            landToLoco.hasExitTime = true;
            landToLoco.exitTime = 0.8f;
            landToLoco.duration = 0.2f;
        }
        else
        {
            AnimatorStateTransition jumpToLoco = jumpState.AddTransition(locomotion);
            jumpToLoco.hasExitTime = false;
            jumpToLoco.duration = 0.15f;
            jumpToLoco.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        }

        // --- One-shot leaps that return to Locomotion once grounded ---
        AddLeapState(rootSm, locomotion, "LongJump", longJump, 450, 50);
        AddLeapState(rootSm, locomotion, "Backflip", backflip, 450, 150);
        AddLeapState(rootSm, locomotion, "SideFlip", sideFlip, 450, 250);
        AddLeapState(rootSm, locomotion, "Dive", dive, 450, 350);

        // --- Rolling (loops while Rolling == true) ---
        if (rolling != null)
        {
            AnimatorState rollState = AddState(rootSm, "Rolling", rolling, 250, 150);

            AnimatorStateTransition toRoll = locomotion.AddTransition(rollState);
            toRoll.hasExitTime = false;
            toRoll.duration = 0.05f;
            toRoll.AddCondition(AnimatorConditionMode.If, 0, "Rolling");

            AnimatorStateTransition fromRoll = rollState.AddTransition(locomotion);
            fromRoll.hasExitTime = false;
            fromRoll.duration = 0.1f;
            fromRoll.AddCondition(AnimatorConditionMode.IfNot, 0, "Rolling");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Animator animator = modelTransform.GetComponent<Animator>();
        if (animator == null) animator = modelTransform.gameObject.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        EditorUtility.SetDirty(animator);

        Movement movement = marioRoot.GetComponent<Movement>();
        if (movement != null)
        {
            SerializedObject so = new SerializedObject(movement);
            SerializedProperty animatorProp = so.FindProperty("animator");
            if (animatorProp != null)
            {
                animatorProp.objectReferenceValue = animator;
                so.ApplyModifiedProperties();
            }
        }

        EditorUtility.SetDirty(marioRoot);
        EditorSceneManager.MarkSceneDirty(marioRoot.scene);
        EditorSceneManager.SaveScene(marioRoot.scene);

        string missing = (run == null ? "Run " : "") + (land == null ? "Land " : "") +
                          (longJump == null ? "LongJump " : "") + (dive == null ? "Dive " : "") +
                          (rolling == null ? "Rolling " : "") + (sideFlip == null ? "SideFlip " : "") +
                          (backflip == null ? "Backflip " : "");

        Debug.Log("PlayerAnimatorSetup: done. Controller saved at " + ControllerPath + " and assigned to the " +
                   "Animator on " + MarioObjectName + "/" + ModelChildName + "." +
                   (missing.Length > 0 ? " Skipped (clip not found): " + missing.Trim() + "." : "") +
                   " Press Play and move around.");
    }

    private static Dictionary<string, AnimationClip> LoadClips()
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        foreach (Object asset in assets)
        {
            if (asset is AnimationClip clip && !result.ContainsKey(clip.name))
            {
                result.Add(clip.name, clip);
            }
        }
        return result;
    }

    private static Avatar LoadAvatar()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        foreach (Object asset in assets)
        {
            if (asset is Avatar avatar)
            {
                return avatar;
            }
        }
        return null;
    }

    private static AnimationClip RequireClip(Dictionary<string, AnimationClip> clips, string name)
    {
        clips.TryGetValue(name, out AnimationClip clip);
        if (clip == null)
        {
            Debug.LogError("PlayerAnimatorSetup: required clip '" + name + "' not found in " + FbxPath);
        }
        return clip;
    }

    private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, float x, float y)
    {
        AnimatorState state = sm.AddState(name, new Vector3(x, y, 0));
        state.motion = motion;
        return state;
    }

    private static void AddAnyStateTrigger(AnimatorStateMachine sm, AnimatorState target, string triggerName)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(target);
        t.hasExitTime = false;
        t.duration = 0.1f;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0, triggerName);
    }

    private static void AddLeapState(AnimatorStateMachine sm, AnimatorState locomotion, string paramName, AnimationClip clip, float x, float y)
    {
        if (clip == null) return;

        AnimatorState state = AddState(sm, paramName, clip, x, y);
        AddAnyStateTrigger(sm, state, paramName);

        AnimatorStateTransition back = state.AddTransition(locomotion);
        back.hasExitTime = false;
        back.duration = 0.15f;
        back.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
    }
}
