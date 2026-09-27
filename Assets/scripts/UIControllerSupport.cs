using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Makes a UI panel (menu, pause screen, etc.) properly navigable with a
/// controller using Unity's New Input System.
///
/// Unity's UI Navigation (Tab/Explicit/Automatic on each Selectable) already
/// handles moving between buttons once something is selected — the two gaps
/// this script fills are: (1) nothing is selected when the panel first opens,
/// so the first controller press does nothing, and (2) the New Input System
/// clears the current selection whenever the mouse moves even slightly, which
/// silently breaks controller navigation until the player clicks something
/// with a mouse again. This script fixes both.
///
/// REQUIRED ONE-TIME PROJECT SETUP (not code):
/// 1. Install the Input System package (Window > Package Manager > Input
///    System) if you haven't already.
/// 2. Project Settings > Player > Active Input Handling: set to
///    "Input System Package (New)" or "Both".
/// 3. Your EventSystem GameObject needs an "Input System UI Input Module"
///    component instead of (or alongside replacing) the old "Standalone
///    Input Module" — Unity adds this automatically when you create a new
///    EventSystem after installing the package. Its "Actions" field should
///    point at an Input Actions asset with a "UI" action map containing
///    Navigate/Submit/Cancel/Point/Click (Unity's default template has this
///    out of the box).
/// 4. On each Button/Selectable, the built-in Navigation setting (visible at
///    the bottom of the Button component, or via Inspector > Navigation)
///    controls which button is selected next on each D-pad/stick direction —
///    "Automatic" works for simple grid/vertical layouts.
///
/// USAGE:
/// 1. Attach this script to the root of a menu/panel GameObject.
/// 2. Assign First Selected to whichever button should be highlighted when
///    the panel opens (e.g. "Play" on the main menu).
/// </summary>
public class UIControllerSupport : MonoBehaviour
{
    [Tooltip("The button/selectable highlighted by default when this panel becomes active.")]
    [SerializeField] private GameObject firstSelected;

    private GameObject lastSelected;

    private void OnEnable()
    {
        // Selecting on the next frame avoids a race with EventSystem
        // clearing selection right as the panel activates.
        lastSelected = firstSelected;
        StartCoroutine(SelectNextFrame(firstSelected));
    }

    private System.Collections.IEnumerator SelectNextFrame(GameObject target)
    {
        yield return null;
        Select(target);
    }

    private void Update()
    {
        if (EventSystem.current == null) return;

        GameObject current = EventSystem.current.currentSelectedGameObject;

        if (current != null)
        {
            // Remember the last thing the player actually had highlighted,
            // so if selection gets cleared we can restore it instead of
            // always jumping back to the very first button.
            lastSelected = current;
            return;
        }

        // Selection was lost (e.g. mouse moved under the New Input System,
        // or the previously-selected button was destroyed/disabled). Any
        // gamepad/keyboard input should bring the highlight back.
        if (GamepadOrKeyboardInputDetected())
        {
            Select(lastSelected != null ? lastSelected : firstSelected);
        }
    }

    private void Select(GameObject target)
    {
        if (target == null || EventSystem.current == null) return;
        EventSystem.current.SetSelectedGameObject(target);
    }

    private bool GamepadOrKeyboardInputDetected()
    {
        var gamepad = UnityEngine.InputSystem.Gamepad.current;
        if (gamepad != null)
        {
            if (gamepad.leftStick.ReadValue().sqrMagnitude > 0.1f) return true;
            if (gamepad.dpad.ReadValue().sqrMagnitude > 0.1f) return true;
            if (gamepad.buttonSouth.wasPressedThisFrame) return true;
        }

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame) return true;
            if (keyboard.downArrowKey.wasPressedThisFrame) return true;
            if (keyboard.leftArrowKey.wasPressedThisFrame) return true;
            if (keyboard.rightArrowKey.wasPressedThisFrame) return true;
        }

        return false;
    }
}
