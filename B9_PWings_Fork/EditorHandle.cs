/*
    This plugin is attached to a handle object
    Initially added by CarnationRED
*/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingProcedural
{
    [DefaultExecutionOrder(65535)]
    public class EditorHandle : MonoBehaviour
    {

        public static EditorHandle draggingHandle;
        public static bool AnyHandleDragging => draggingHandle && draggingHandle.dragging;

        public static float Gain = 0.0125f;

        /// <summary>
        /// 0:None, 1:X, 2:Y
        /// </summary>
        private int axisLockState;
        /// <summary>
        /// far: quicker, near: slower
        /// </summary>
        private float cameraDistanceGain = 1f;
        /// <summary>
        /// Incremental values
        /// </summary>
        public float axisX, axisY;
        /// <summary>
        /// Delta values 
        /// </summary>
        public float deltaAxisX, deltaAxisY;
        /// <summary>
        /// Delta values, but affected by axisLockState
        /// </summary>
        public float LockDeltaAxisX => axisLockState == 1 ? 0 : deltaAxisX * MouseSensitivity;
        /// <summary>
        /// Delta values, but affected by axisLockState
        /// </summary>
        public float LockDeltaAxisY => axisLockState == 2 ? 0 : deltaAxisY * MouseSensitivity;

        /// <summary>
        /// How sensitive the mouse is
        /// </summary>
        float MouseSensitivity => (float)HighLogic.CurrentGame.Parameters.CustomParams<WPSensitivity>().mouseSensitivity;
        void OnGUI()
        {
            if (!dragging || !HighLogic.LoadedSceneIsEditor || !WingProcedural.uiWindowActive) return;
            if (axisLockState == 0)
            {
                LineDrawer.Instance.enabled = false;
                return;
            }

            var dir = axisLockState == 2 ? transform.right : transform.up;
            dir *= 40f;
            var color = axisLockState == 2 ? Color.red : Color.green;
            DrawLine(transform.position + dir, transform.position - dir, color);
        }

        private static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            LineDrawer.Instance.enabled = true;
            LineDrawer.color = color;
            LineDrawer.start = start;
            LineDrawer.end = end;
        }

        void Update()
        {
            if(!HighLogic.LoadedSceneIsEditor)
                return;

            // A temporary fix for a weird bug, can't figure out where the layer is changed in some situations
            if (gameObject.layer == 0)
                gameObject.layer = 2;

            if (!dragging)
                return;
            if (Input.GetMouseButtonUp(0))
            {
                LineDrawer.Instance.enabled = dragging = false;
                axisX = axisY = 0;
                return;
            }

            bool x = Input.GetKeyDown(KeyCode.X);
            bool y = Input.GetKeyDown(KeyCode.Y);
            if (x || y)
                switch (axisLockState)
                {
                    case 0:
                        if (x) axisLockState = 1;
                        else if (y) axisLockState = 2;
                        break;
                    case 1:
                        if (x) axisLockState = 0;
                        else if (y) axisLockState = 2;
                        break;
                    case 2:
                        if (x) axisLockState = 1;
                        else if (y) axisLockState = 0;
                        break;
                    default:
                        break;
                }

            var offset = Input.mousePosition - mouseLastPos;

            var yProjected = (offset.y - axisXProjectedToScreen.y * offset.x / axisXProjectedToScreen.x) / ((axisYProjectedToScreen.y - axisXProjectedToScreen.y * axisYProjectedToScreen.x / axisXProjectedToScreen.x));
            var xProjected = (offset.x - yProjected * axisYProjectedToScreen.x) / axisXProjectedToScreen.x;

            var speedMult = MathD.Clamp(offset.sqrMagnitude * .025f, 0.4f, 1.25f);
            axisX = Gain * speedMult * cameraDistanceGain * xProjected;
            axisY = Gain * speedMult * cameraDistanceGain * yProjected;
            deltaAxisX += axisX;
            deltaAxisY += axisY;

            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftCommand))
            {
                if (axisLockState == 0)
                    axisLockState = Math.Abs(deltaAxisY) < Math.Abs(deltaAxisX) ? 2 : 1;
                else if (axisLockState == 1 && Math.Abs(deltaAxisX)>.5f)
                    axisLockState = Math.Abs(deltaAxisY) < Math.Abs(deltaAxisX)*.75f ? 2 : 1;
                else if (axisLockState == 2 && Math.Abs(deltaAxisY)>.5f)
                    axisLockState = Math.Abs(deltaAxisY)*.75f < Math.Abs(deltaAxisX) ? 2 : 1;
            }
            else if (Input.GetKeyUp(KeyCode.LeftControl) || Input.GetKeyUp(KeyCode.LeftCommand))
                axisLockState = 0;

            mouseLastPos = Input.mousePosition;
        }
        public bool dragging = false;
        private Vector3 mouseLastPos;
        private Vector3 axisXProjectedToScreen;
        private Vector3 axisYProjectedToScreen;

        public void OnMouseOver()
        {
            deltaAxisX = deltaAxisY = 0;
            dragging = true;
            draggingHandle = this;
            mouseLastPos = Input.mousePosition;
            axisXProjectedToScreen = Camera.main.worldToCameraMatrix.MultiplyVector(transform.right);
            axisYProjectedToScreen = Camera.main.worldToCameraMatrix.MultiplyVector(transform.up);
            axisXProjectedToScreen.z = 0;
            axisYProjectedToScreen.z = 0;
            axisXProjectedToScreen.Scale(Vector3.one / Mathf.Max(0.01f, axisXProjectedToScreen.magnitude));
            axisYProjectedToScreen.Scale(Vector3.one / Mathf.Max(0.01f, axisYProjectedToScreen.magnitude));
            cameraDistanceGain = MathD.Clamp(Mathf.Abs(Camera.main.worldToCameraMatrix.MultiplyPoint3x4(transform.position).z) * .02f, 0.0125f, 2f);
            axisLockState = 0;
        }
    }
    public class LineDrawer : MonoBehaviour
    {
        private static LineDrawer instance;

        public static LineDrawer Instance
        {
            get
            {
                if (!instance)
                {
                    var main = EditorLogic.fetch.editorCamera;
                    if (main.TryGetComponent(out LineDrawer p))
                        return instance = p;
                    instance = main.gameObject.AddComponent<LineDrawer>();
                    if (!material) material = new Material(StaticWingGlobals.handleLength.GetComponent<MeshRenderer>().sharedMaterial);
                    instance.enabled = false;
                }
                return instance;
            }
        }
        private static Material material;
        public static Color color;

        public static Vector3 start, end;

        private void OnPostRender()
        {
            GL.PushMatrix();
            material.color = color;
            material.SetPass(0);
            GL.Begin(GL.LINES);
            GL.Vertex(start);
            GL.Vertex(end);
            GL.End();
            GL.PopMatrix();
        }
    }
}