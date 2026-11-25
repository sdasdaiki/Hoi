using UnityEngine;
using UnityEngine.EventSystems;

namespace EdoStrategy.UI
{
    public class BattleCameraController : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float minOrthographicSize = 2f;
        [SerializeField] private float maxOrthographicSize = 12f;
        [SerializeField] private float zoomSensitivity = 0.16f;
        [SerializeField] private float zoomSmoothness = 12f;

        private float targetOrthographicSize;
        private Vector3 targetPosition;
        private Vector3 previousMouseWorldPosition;

        public bool IsPanning { get; private set; }

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
            if (targetCamera != null)
            {
                targetOrthographicSize = targetCamera.orthographicSize;
                targetPosition = targetCamera.transform.position;
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                return;
            }

            UpdatePan();
            UpdateZoomInput();
            SmoothZoom();
        }

        private void UpdatePan()
        {
            bool pointerOverUi =
                EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();

            if (!IsPanning &&
                Input.GetMouseButtonDown(2) &&
                !pointerOverUi &&
                TryGetMouseWorldPosition(out previousMouseWorldPosition))
            {
                IsPanning = true;
            }

            if (!Input.GetMouseButton(2))
            {
                IsPanning = false;
                return;
            }
            if (!IsPanning ||
                !TryGetMouseWorldPosition(out Vector3 currentMouseWorldPosition))
            {
                return;
            }

            Vector3 movement = previousMouseWorldPosition -
                               currentMouseWorldPosition;
            targetCamera.transform.position += movement;
            targetPosition = targetCamera.transform.position;
            if (!TryGetMouseWorldPosition(out previousMouseWorldPosition))
            {
                previousMouseWorldPosition = currentMouseWorldPosition;
            }
        }

        private void UpdateZoomInput()
        {
            if (IsPointerOverUi())
            {
                return;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f) ||
                !TryGetMouseWorldPosition(out Vector3 cursorWorldPosition))
            {
                return;
            }

            float previousSize = targetOrthographicSize;
            targetOrthographicSize = Mathf.Clamp(
                previousSize * Mathf.Exp(-scroll * zoomSensitivity),
                minOrthographicSize,
                maxOrthographicSize);
            if (Mathf.Approximately(previousSize, targetOrthographicSize))
            {
                return;
            }

            float sizeRatio = targetOrthographicSize /
                              targetCamera.orthographicSize;
            Vector3 cameraPosition = targetCamera.transform.position;
            targetPosition = cursorWorldPosition -
                (cursorWorldPosition - cameraPosition) * sizeRatio;
        }

        private void SmoothZoom()
        {
            float interpolation = 1f -
                Mathf.Exp(-zoomSmoothness * Time.unscaledDeltaTime);
            targetCamera.orthographicSize = Mathf.Lerp(
                targetCamera.orthographicSize,
                targetOrthographicSize,
                interpolation);
            targetCamera.transform.position = Vector3.Lerp(
                targetCamera.transform.position,
                targetPosition,
                interpolation);
        }

        private bool TryGetMouseWorldPosition(out Vector3 worldPosition)
        {
            Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
            Plane mapPlane = new Plane(Vector3.forward, Vector3.zero);
            if (mapPlane.Raycast(ray, out float distance))
            {
                worldPosition = ray.GetPoint(distance);
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null &&
                   EventSystem.current.IsPointerOverGameObject();
        }
    }
}
