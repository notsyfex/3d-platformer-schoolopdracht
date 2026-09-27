using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Fires an event when the player presses "Cancel/Back" on a controller
/// (East/B button on Xbox-style pads, Circle on PlayStation pads) or Escape
/// on keyboard, while this panel is active — e.g. to close a pause menu or
/// go back a level-select screen.
///
/// USAGE:
/// 1. Attach to the same panel as UIControllerSupport (or any active menu
///    panel).
/// 2. Wire onCancel in the Inspector to whatever should happen — closing
///    the panel, calling SceneManager.LoadScene("MainMenu"), etc.
/// </summary>
public class MenuCancelHandler : MonoBehaviour
{
    [Tooltip("Fired when Cancel/Back is pressed (controller East/B button, PlayStation Circle, or Escape).")]
    public UnityEvent onCancel;

    private void Update()
    {
        var gamepad = Gamepad.current;
        if (gamepad != null && gamepad.buttonEast.wasPressedThisFrame)
        {
            onCancel?.Invoke();
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            onCancel?.Invoke();
        }
    }
}
