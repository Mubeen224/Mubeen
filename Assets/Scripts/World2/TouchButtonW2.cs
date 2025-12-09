using UnityEngine;
using UnityEngine.EventSystems;

namespace Mankibo
{
    public class TouchButtonW2 : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public enum ButtonType { Left, Right }
        public ButtonType buttonType;

        public World1Controller controller;

        private bool isPressed = false;

        private void Update()
        {
            // If user is pressing, keep sending movement every frame
            if (isPressed)
            {
                Vector2 direction = buttonType == ButtonType.Left ? Vector2.left : Vector2.right;
                controller?.StartMove(direction);
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            // Extra safety for mouse: stop when mouse is released anywhere
            if (isPressed && !Input.GetMouseButton(0))
            {
                isPressed = false;
                controller?.StopMove();
            }
#endif
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            controller?.StopMove();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // In case user drags finger/mouse off the button
            isPressed = false;
            controller?.StopMove();
        }
    }
}
