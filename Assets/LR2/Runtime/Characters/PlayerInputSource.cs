using LR2.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LR2.Characters
{
    /// <summary>
    /// Keyboard/mouse + gamepad bindings for the local player (Input System, defined in code so nothing can be
    /// lost in an asset). Produces <see cref="SoldierInput"/> for the SoldierController.
    /// </summary>
    [RequireComponent(typeof(SoldierController))]
    public class PlayerInputSource : MonoBehaviour, ISoldierInputSource
    {
        [Header("Mouse")]
        [Tooltip("Degrees of rotation per mouse count at sensitivity 1.0 (0.07 = snappy CoD-style 1:1 mouse feel).")]
        [SerializeField] private float degreesPerCount = 0.07f;

        [Header("Gamepad")]
        [SerializeField] private float gamepadYawSpeed = 260f;
        [SerializeField] private float gamepadPitchSpeed = 180f;
        [SerializeField, Range(0f, 0.5f)] private float stickDeadzone = 0.12f;

        [Header("Behaviour")]
        [SerializeField] private bool toggleAim;
        [SerializeField] private bool toggleSprint;
        [Tooltip("Sprint automatically while moving forward (no key needed).")]
        [SerializeField] private bool autoSprint;

        private InputAction _move, _look, _lookStick, _fire, _aim, _sprint, _jump, _crouch, _reload, _leanL, _leanR,
            _slot1, _slot2, _next, _scroll, _interact;
        private bool _aimToggled, _sprintToggled;

        private void OnEnable()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");

            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");
            _fire = new InputAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
            _fire.AddBinding("<Gamepad>/rightTrigger");
            _aim = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton");
            _aim.AddBinding("<Gamepad>/leftTrigger");
            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");
            _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");
            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Gamepad>/buttonEast");
            _reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            _reload.AddBinding("<Gamepad>/buttonWest");
            _leanL = new InputAction("LeanLeft", InputActionType.Button, "<Keyboard>/q");
            _leanR = new InputAction("LeanRight", InputActionType.Button, "<Keyboard>/e");
            _slot1 = new InputAction("Slot1", InputActionType.Button, "<Keyboard>/1");
            _slot2 = new InputAction("Slot2", InputActionType.Button, "<Keyboard>/2");
            _next = new InputAction("NextWeapon", InputActionType.Button, "<Gamepad>/buttonNorth");
            _scroll = new InputAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");
            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/f");

            foreach (var a in All()) a.Enable();
        }

        private void OnDisable()
        {
            foreach (var a in All())
            {
                if (a == null) continue;
                a.Disable();
                a.Dispose();
            }
        }

        private InputAction[] All() => new[]
        {
            _move, _look, _lookStick, _fire, _aim, _sprint, _jump, _crouch, _reload, _leanL, _leanR, _slot1, _slot2, _next,
            _scroll, _interact
        };

        public void GetInput(SoldierController soldier, ref SoldierInput input)
        {
            bool gameplayFocused = Cursor.lockState == CursorLockMode.Locked && Time.timeScale > 0f;
            if (!gameplayFocused)
            {
                input.ClearAll();
                return;
            }

            input.absoluteLook = false;
            input.move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);

            float sens = LR2Settings.MouseSensitivity * (soldier.IsAiming ? LR2Settings.AdsSensitivityMultiplier * soldier.ScopeSensitivity : 1f);
            Vector2 mouse = _look.ReadValue<Vector2>() * (degreesPerCount * sens);
            Vector2 stick = _lookStick.ReadValue<Vector2>();
            if (stick.magnitude < stickDeadzone) stick = Vector2.zero;
            stick = stick * stick.magnitude; // response curve
            Vector2 pad = new Vector2(stick.x * gamepadYawSpeed, stick.y * gamepadPitchSpeed) * (Time.deltaTime * sens);
            Vector2 look = mouse + pad;
            input.look = new Vector2(look.x, LR2Settings.InvertY ? look.y : -look.y);

            input.fire = _fire.IsPressed();
            if (toggleAim)
            {
                if (_aim.WasPressedThisFrame()) _aimToggled = !_aimToggled;
                input.aim = _aimToggled;
            }
            else input.aim = _aim.IsPressed();

            if (autoSprint) input.sprint = input.move.y > 0.7f;
            else if (toggleSprint)
            {
                if (_sprint.WasPressedThisFrame()) _sprintToggled = !_sprintToggled;
                if (input.move.y < 0.3f) _sprintToggled = false;
                input.sprint = _sprintToggled;
            }
            else input.sprint = _sprint.IsPressed();

            input.jump |= _jump.WasPressedThisFrame();
            input.crouch |= _crouch.WasPressedThisFrame();
            input.reload |= _reload.WasPressedThisFrame();
            input.interact |= _interact.WasPressedThisFrame();
            input.lean = (_leanR.IsPressed() ? 1f : 0f) - (_leanL.IsPressed() ? 1f : 0f);

            if (_slot1.WasPressedThisFrame()) input.weaponSlot = 0;
            if (_slot2.WasPressedThisFrame()) input.weaponSlot = 1;
            float scroll = _scroll.ReadValue<float>();
            input.nextWeapon |= _next.WasPressedThisFrame() || Mathf.Abs(scroll) > 0.1f;
        }
    }
}
