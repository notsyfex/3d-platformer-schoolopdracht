using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// EDITOR-ONLY tool. Must live in a folder named "Editor" (e.g. Assets/scripts/Editor/)
/// so Unity excludes it from game builds.
///
/// Swaps the visual model on "full player.prefab" from the mismatched Ch19 model to
/// the Mario model that ships with the animations in Assets/animations/Player/Mario,
/// and assigns the existing PlayerAnimatorController (built by PlayerAnimatorSetup)
/// so the Player@... clips - which were built for this exact Mario rig - actually play.
///
/// The Mario.FBX asset also carries the Hello Mario Framework's OWN costume/powerup
/// meshes (Fire, Raccoon, Boomerang, Metal, several alternate face/hand poses) plus its
/// own CharacterController + Movement script baked onto the prefab root. Left alone
/// those make the swapped-in Mario look "bulky"/faceless (many costume meshes visible
/// at once) and move "weird" (two CharacterControllers/Movement scripts driving the
/// same object). This tool strips both down to a single default costume and a single
/// set of movement components (the ones already on "character").
///
/// HOW TO RUN:
/// 1. Drop this file in Assets/scripts/Editor/ and let Unity compile.
/// 2. Tools > Player > Swap Visual To Mario.
/// 3. Check the Console for a success message.
/// </summary>
public static class SwapToMarioModel
{
    private const string PrefabPath = "Assets/full player.prefab";
    private const string MarioFbxPath = "Assets/animations/Player/Mario/Mesh/Mario.FBX";
    private const string ControllerPath = "Assets/animations/Player/PlayerAnimatorController.controller";
    private const string MatFolder = "Assets/animations/Player/Mario/Mat";

    // Substrings that mark a renderer/child as belonging to a non-default costume or
    // power-up variant (Fire Mario, Raccoon/Tanooki Mario, Boomerang Mario, Metal Mario).
    // Anything whose name contains one of these gets disabled outright.
    private static readonly string[] PowerupKeywords =
    {
        "Boomerang", "Metal", "Raccoon", "Fire", "Suit", "Tail"
    };

    [MenuItem("Tools/Player/Swap Visual To Mario")]
    public static void Swap()
    {
        GameObject marioAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MarioFbxPath);
        if (marioAsset == null)
        {
            Debug.LogError("SwapToMarioModel: couldn't find Mario model at " + MarioFbxPath);
            return;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("SwapToMarioModel: couldn't find " + ControllerPath +
                            ". Run Tools > Player > Generate Animator Controller first.");
            return;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform character = FindDeepChild(prefabRoot.transform, "character");
            if (character == null)
            {
                Debug.LogError("SwapToMarioModel: couldn't find a 'character' child in " + PrefabPath);
                return;
            }

            // Hide (don't destroy, in case you want to revert) the old mismatched visual.
            int hidden = 0;
            foreach (SkinnedMeshRenderer smr in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.gameObject.SetActive(false);
                hidden++;
            }

            // Remove any stray Animator sitting directly on "character" itself (added when
            // character.fbx was converted to Humanoid) so it doesn't fight with Mario's Animator.
            Animator staleAnimator = character.GetComponent<Animator>();
            if (staleAnimator != null)
            {
                Object.DestroyImmediate(staleAnimator, true);
            }

            // Avoid piling up duplicate Mario instances if this is run more than once.
            Transform existingMario = character.Find("Mario");
            if (existingMario != null)
            {
                Object.DestroyImmediate(existingMario.gameObject, true);
            }

            GameObject marioInstance = (GameObject)PrefabUtility.InstantiatePrefab(marioAsset, character);
            marioInstance.name = "Mario";
            marioInstance.transform.localPosition = Vector3.zero;
            marioInstance.transform.localRotation = Quaternion.identity;
            marioInstance.transform.localScale = Vector3.one;

            // The Hello Mario Framework prefab ships with its own CharacterController +
            // Movement script on the root. We already have those on "character" - remove
            // the duplicates so only one set drives movement.
            int strippedControllers = StripDuplicateControllers(marioInstance);

            // Disable every costume/power-up mesh variant (Fire/Raccoon/Boomerang/Metal/etc.)
            // and every duplicate numbered face/hand pose, keeping only one default set visible.
            int trimmed = TrimToDefaultCostume(marioInstance);

            Animator animator = marioInstance.GetComponent<Animator>();
            if (animator == null) animator = marioInstance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            int remapped = RemapMaterials(marioInstance);

            EditorUtility.SetDirty(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);

            Debug.Log("SwapToMarioModel: done. Hid " + hidden + " old renderer(s), added Mario under 'character', " +
                       "removed " + strippedControllers + " duplicate movement component(s), disabled " + trimmed +
                       " extra costume/pose mesh(es), remapped " + remapped + " material slot(s), and assigned " +
                       "PlayerAnimatorController. Press Play and move around.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // The Hello Mario Framework's own Mario.prefab (which Mario.FBX was extracted from) carries
    // its own CharacterController and "Movement" script for standalone use. Our rig already has
    // both on the parent "character" GameObject, so having a second copy here means two
    // controllers/scripts independently trying to move the same object every frame - which is
    // exactly the kind of thing that produces "weird" movement/animation. Strip them.
    private static int StripDuplicateControllers(GameObject marioInstance)
    {
        int removed = 0;

        foreach (CharacterController cc in marioInstance.GetComponentsInChildren<CharacterController>(true))
        {
            Object.DestroyImmediate(cc, true);
            removed++;
        }

        // Match by component type name rather than a compile-time reference, since the
        // framework's "Movement" script (and any similar controller/input script it bundles)
        // isn't a type this Editor assembly references.
        string[] unwantedTypeNames = { "Movement", "PlayerController", "PlayerMovement", "InputController" };
        foreach (Transform t in marioInstance.GetComponentsInChildren<Transform>(true))
        {
            foreach (Component comp in t.GetComponents<Component>())
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                foreach (string unwanted in unwantedTypeNames)
                {
                    if (typeName == unwanted)
                    {
                        Object.DestroyImmediate(comp, true);
                        removed++;
                        break;
                    }
                }
            }
        }

        return removed;
    }

    // Mario.FBX contains every costume Mario can wear (default, Fire, Raccoon/Tanooki,
    // Boomerang, Metal) plus several numbered alternate face/hand pose frames (Face00..Face08,
    // HandL00..HandL07, HandR00..HandR07, Eyelid00/01), all as separate child meshes under the
    // same rig, all active at once by default. That's what makes the swapped-in Mario look
    // "bulky"/faceless - e.g. all 9 face expressions and both eyelid states rendering on top
    // of each other simultaneously. Keep exactly one default variant per group.
    private static int TrimToDefaultCostume(GameObject root)
    {
        int disabled = 0;
        List<Renderer> remaining = new List<Renderer>();

        // First pass: drop costume/power-up meshes outright by name, wherever they sit in the
        // hierarchy (their own renderer name always carries the power-up's name).
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            bool isPowerup = false;
            foreach (string kw in PowerupKeywords)
            {
                if (r.name.IndexOf(kw, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isPowerup = true;
                    break;
                }
            }

            if (isPowerup)
            {
                if (r.gameObject.activeSelf)
                {
                    r.gameObject.SetActive(false);
                    disabled++;
                }
                continue;
            }

            remaining.Add(r);
        }

        // Second pass: the pose-frame meshes (Face/Hand/Eyelid) are named after the material
        // they use (e.g. "Face00__MarioFaceMat00" and "Face00__MarioHigeMat00" are TWO
        // different meshes that belong to the SAME pose), so grouping by the renderer's own
        // name doesn't identify duplicates. Each one is skinned to a bone named after its pose
        // (e.g. "Face00", "HandL03", "Eyelid01") via SkinnedMeshRenderer.rootBone - that bone
        // name is what actually identifies the pose, so group by it instead. Strip the bone
        // name's trailing digits to get its category ("Face00" -> "Face"), keep every renderer
        // whose bone matches the lowest-numbered bone in that category, and disable the rest.
        Dictionary<string, List<Renderer>> byBone = new Dictionary<string, List<Renderer>>();
        foreach (Renderer r in remaining)
        {
            string boneName = r.name;
            if (r is SkinnedMeshRenderer smr && smr.rootBone != null)
            {
                boneName = smr.rootBone.name;
            }

            if (!byBone.TryGetValue(boneName, out List<Renderer> list))
            {
                list = new List<Renderer>();
                byBone[boneName] = list;
            }
            list.Add(r);
        }

        Dictionary<string, List<string>> categoryToBones = new Dictionary<string, List<string>>();
        foreach (string boneName in byBone.Keys)
        {
            int i = boneName.Length;
            while (i > 0 && char.IsDigit(boneName[i - 1])) i--;
            string category = i == boneName.Length ? boneName : boneName.Substring(0, i);

            if (!categoryToBones.TryGetValue(category, out List<string> bones))
            {
                bones = new List<string>();
                categoryToBones[category] = bones;
            }
            bones.Add(boneName);
        }

        foreach (KeyValuePair<string, List<string>> kvp in categoryToBones)
        {
            if (kvp.Value.Count <= 1) continue; // no alternate-numbered sibling, nothing to trim

            kvp.Value.Sort((a, b) => string.CompareOrdinal(a, b));
            for (int i = 1; i < kvp.Value.Count; i++)
            {
                foreach (Renderer r in byBone[kvp.Value[i]])
                {
                    if (r.gameObject.activeSelf)
                    {
                        r.gameObject.SetActive(false);
                        disabled++;
                    }
                }
            }
        }

        return disabled;
    }

    // The FBX is imported with "Use Embedded Materials" (Unity 6 dropped external material
    // remapping), so the renderers come in with plain default materials named after their
    // original slots (e.g. "MarioBodyMat00"). Swap those for the real .mat assets we imported
    // alongside the model so Mario isn't pink/gray in the scene.
    private static int RemapMaterials(GameObject root)
    {
        Dictionary<string, Material> materialsByName = new Dictionary<string, Material>();
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MatFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) materialsByName[mat.name] = mat;
        }

        int remapped = 0;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string cleanName = mats[i].name.Replace(" (Instance)", "").Trim();
                if (materialsByName.TryGetValue(cleanName, out Material replacement) && replacement != mats[i])
                {
                    mats[i] = replacement;
                    changed = true;
                    remapped++;
                }
            }
            if (changed) renderer.sharedMaterials = mats;
        }
        return remapped;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}
