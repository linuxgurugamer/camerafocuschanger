//#define debugCFC

using System;
using System.IO;
using System.Diagnostics;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using KSP.IO;


namespace CameraFocusChanger
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CameraFocusChanger : MonoBehaviour
    {
        FlightCamera flightCamera;
        float pivotTranslateSharpness;
        Transform targetTransform;
        UnityEngine.KeyCode actionKey = KeyCode.O;
        enum SnapMode { smooth, stock, hybrid };
        SnapMode snapMode = SnapMode.smooth;

        float startFocusTime;
        bool hasReachedTarget;
        bool isFocusing;
        bool showUpdateMessage = true;

        [ConditionalAttribute("DEBUG")]
        void DebugPrint(string text)
        {
            print("[CFC] " + text);
        }

        public static readonly String ROOT_PATH = KSPUtil.ApplicationRootPath;
        private static readonly String CONFIG_BASE_FOLDER = ROOT_PATH + "GameData/";
        private static String CFC_BASE_FOLDER = CONFIG_BASE_FOLDER + "CameraFocusChanger/";
        private static String CFC_CFG_FILE = CFC_BASE_FOLDER + "PluginData/CameraFocusChanger.cfg";
        private static string CFC_NODE = "CameraFocusChager";


        static string SafeLoad(string value, string oldvalue)
        {
            if (value == null)
                return oldvalue.ToString();
            return value;
        }

        bool LoadCfg()
        {
            ConfigNode file;

            if (System.IO.File.Exists(CFC_CFG_FILE))
                file = ConfigNode.Load(CFC_CFG_FILE);
            else
                return false;

            ConfigNode node = file.GetNode(CFC_NODE);
            if (node != null)
            {
                actionKey = (KeyCode)System.Enum.Parse(typeof(KeyCode), SafeLoad(node.GetValue("actionKey"), "O"));
                snapMode = (SnapMode)System.Enum.Parse(typeof(SnapMode), SafeLoad(node.GetValue("snapMode"), SnapMode.smooth.ToString()));
                DebugPrint("LoadCfg, actionKey: " + actionKey.ToString() + ", snapMode: " + snapMode.ToString());
            }
            return true;
        }
        void SaveCfg()
        {
            ConfigNode file = new ConfigNode();
            ConfigNode node = new ConfigNode();
            node.AddValue("actionKey", actionKey.ToString());
            node.AddValue("snapMode", snapMode.ToString());
            file.AddNode(CFC_NODE, node);
            file.Save(CFC_CFG_FILE);

        }
           
        void Start()
        {
            DebugPrint("Starting Camera Focus Changer");

            flightCamera = FlightCamera.fetch;
            pivotTranslateSharpness = 0.5f;
            hasReachedTarget = false;
            isFocusing = false;

            PluginConfiguration config = PluginConfiguration.CreateForType<CameraFocusChanger>();
            config.load();
            //actionKey = config.GetValue<KeyCode>("actionKey", KeyCode.O);


            if (!LoadCfg())
                SaveCfg();
            showUpdateMessage = config.GetValue<bool>("showUpdateMessage", true);

            GameEvents.OnCameraChange.Add(OnCameraChange);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselGoOnRails.Add(OnVesselGoOnRails);
            GameEvents.onStageSeparation.Add(OnStageSeparation);
            GameEvents.onUndock.Add(OnUndock);

            API.SetInstance(this);
        }

        void OnDestroy()
        {
            DebugPrint("Disabled");

            GameEvents.OnCameraChange.Remove(OnCameraChange);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselGoOnRails.Remove(OnVesselGoOnRails);
            GameEvents.onStageSeparation.Remove(OnStageSeparation);
            GameEvents.onUndock.Remove(OnUndock);

            API.SetInstance(null);
        }

        void OnCameraChange(CameraManager.CameraMode cameraMode)
        {
            DebugPrint(string.Format("camera mode changed to {0}", cameraMode.ToString()));
            if (cameraMode == CameraManager.CameraMode.IVA && targetTransform != null)
            {
                ResetFocus();
            }
        }

        void OnVesselChange(Vessel vessel)
        {
            DebugPrint(string.Format("vessel changed to {0}", vessel.vesselName));
            CheckForVesselChanged();
        }

        void OnVesselWillDestroy(Vessel vessel)
        {
            if (targetTransform != null)
            {
                Part part = Part.FromGO(targetTransform.gameObject);
                if (part != null && part.vessel == vessel)
                {
                    DebugPrint("vessel about to be destroyed");
                    ResetFocus();
                }
            }
        }

        void OnVesselGoOnRails(Vessel vessel)
        {
            if (targetTransform != null)
            {
                Part part = Part.FromGO(targetTransform.gameObject);
                if (part != null && part.vessel == vessel)
                {
                    DebugPrint("vessel about to be packed");
                    ResetFocus();
                }
            }
        }

        void OnStageSeparation(EventReport report)
        {
            CheckForVesselChanged();
        }

        void OnUndock(EventReport report)
        {
            CheckForVesselChanged();
        }

        void CheckForVesselChanged()
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (targetTransform != null)
            {
                Part part = Part.FromGO(targetTransform.gameObject);
                if (part != null && part.vessel != currentVessel)
                {
                    DebugPrint("vessel mismatch");
                    string message = string.Format("CFC WARNING\n!!!Controlled Vessel is not Focussed!!!");
                    var screenMessage = new ScreenMessage(message, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    ScreenMessages.PostScreenMessage(screenMessage);
                }
            }
        }

        void Update()
        {
            // check if we are trying to change the focus
            GameObject obj = EventSystem.current.currentSelectedGameObject;
            bool inputFieldIsFocused = InputLockManager.IsAllLocked(ControlTypes.KEYBOARDINPUT) || (obj != null && obj.GetComponent<InputField>() != null && obj.GetComponent<InputField>().isFocused);
            if (!inputFieldIsFocused && Input.GetKeyDown(actionKey))
            {
                if (GameSettings.MODIFIER_KEY.GetKey())
                {
                    snapMode++;
                    if (snapMode > SnapMode.hybrid)
                        snapMode = SnapMode.smooth;
                    switch (snapMode)
                    {
                         case SnapMode.smooth:
                            ScreenMessages.PostScreenMessage("Camera Focus Changer mode changed to smooth scrolling");
                            break;
                       case SnapMode.stock:
                            ScreenMessages.PostScreenMessage("Camera Focus Changer mode changed to Stock (instant snapping)");
                            break;
                        case SnapMode.hybrid:
                            ScreenMessages.PostScreenMessage("Camera Focus Changer mode changed to hybrid (smooth scrolling, snap back to active)");
                            break;
                    }

                    SaveCfg();

                    return;
                }

                DebugPrint("updating camera focus");

                if ((Time.time - startFocusTime) < 0.25f)
                {
                    hasReachedTarget = true;
                }
                else
                {
                    // find a part under the mouse, if there is one, set it as the camera's point of focus
                    // otherwise, revert to the center of mass
                    Transform raycastTransform = GetTransform();
                    if (raycastTransform != null)
                    {
                        FocusOn(raycastTransform);
                    }
                    else if (targetTransform != null)
                    {
                        ResetFocus();
                    }
                    else
                    {
                        hasReachedTarget = true;
                    }
                }
            }

            if (!inputFieldIsFocused && Input.GetKeyDown(KeyCode.Y))
            {
                DebugPrint(string.Format("target: {0}", targetTransform));
                DebugPrint(string.Format("vessel: {0}", FlightGlobals.ActiveVessel.GetWorldPos3D()));
                DebugPrint(string.Format("camera: {0}", flightCamera.transform.parent.position));
            }

            UpdateFocus();
        }

        public void FocusOn(Transform transform)
        {
            DebugPrint(string.Format("found target {0}", transform.gameObject.name));
            if (flightCamera.pivotTranslateSharpness > 0)
            {
                pivotTranslateSharpness = flightCamera.pivotTranslateSharpness;
                DebugPrint(string.Format("sharpness of {0}", pivotTranslateSharpness));
            }
            flightCamera.pivotTranslateSharpness = 0;

            // targeting the same part twice will make the camera jump to it
            hasReachedTarget = transform == targetTransform;

            if (showUpdateMessage)
            {
                Part part = Part.FromGO(transform.gameObject);
                string message = string.Format("CFC Actived ({0})", part ? part.partInfo.title : "." + transform.gameObject.name);
                var screenMessage = new ScreenMessage(message, 1.5f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(screenMessage);
            }

            startFocusTime = Time.time;
            isFocusing = true;
            targetTransform = transform;
            DebugPrint("FocusOn, activeVessel.currentPosition: " + FlightGlobals.ActiveVessel.GetWorldPos3D() + ", targetTransform.position: " + targetTransform.position);

        }

        public void ResetFocus()
        {
            DebugPrint("Reset Target");
            if (showUpdateMessage)
            {
                var screenMessage = new ScreenMessage("CFC Deactivated", 1.5f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage(screenMessage);
            }

            targetTransform = null;
            hasReachedTarget = false;
            isFocusing = true;
            startFocusTime = Time.time;
            flightCamera.pivotTranslateSharpness = pivotTranslateSharpness;
            DebugPrint("ResetFocus, activeVessel.currentPosition: " + FlightGlobals.ActiveVessel.GetWorldPos3D());
        }

        void UpdateFocus()
        {
            // do we have a target for the camera focus
            if (isFocusing)
            {
                Vessel vessel = FlightGlobals.ActiveVessel;
                Vector3d currentPosition = vessel.GetWorldPos3D();

                Vector3 targetPosition = targetTransform != null ? targetTransform.position : new Vector3((float)currentPosition.x, (float)currentPosition.y, (float)currentPosition.z);

                Vector3 positionDifference = flightCamera.transform.parent.position - targetPosition;
                float distance = positionDifference.magnitude;

                //if (distance >= 0.015f)                    DebugPrint(string.Format("Distance of {0}", distance));
                DebugPrint("UpdateFocus, targetPosition: " + targetPosition + ", flightCamera.transform.parent.position: " + flightCamera.transform.parent.position + ", distance: " + distance);

#if true
                if (snapMode != SnapMode.smooth)
                {
                    // stock
                    if (targetTransform != null)
                    {
                        if (snapMode == SnapMode.stock)
                        {
                            Part part = targetTransform != null ? Part.FromGO(targetTransform.gameObject) : null;
                            if (part != null)
                                FlightCamera.SetTarget(part);
                        }
                    }
                    else
                    {
                        if (FlightGlobals.ActiveVessel != null) // && stockInstant == SnapMode.stock)
                            FlightCamera.SetTarget(FlightGlobals.ActiveVessel);
                    }
                    if (snapMode == SnapMode.stock && targetTransform == null)
                    {
                        hasReachedTarget = true;

                        return;
                    }
                }
#endif

                if (hasReachedTarget || distance < 0.015f)
                {
                    flightCamera.transform.parent.position = targetPosition;
                    hasReachedTarget = true;
                    isFocusing = targetTransform != null;
                    DebugPrint("UpdateFocus 1");
#if false
                    if (targetTransform != null)
                    {

                        Part part = targetTransform != null ? Part.FromGO(targetTransform.gameObject) : null;
                        if (part != null)
                            FlightCamera.SetTarget(part);
                        flightCamera.transform.parent.position = targetPosition;
                    }
                    else
                    {
                        if (FlightGlobals.ActiveVessel != null)
                            FlightCamera.SetTarget(FlightGlobals.ActiveVessel);
                    }
#endif
                    hasReachedTarget = true;

                }
                else
                {
                    DebugPrint(string.Format("Moving by {0}", (positionDifference.normalized * Time.fixedDeltaTime * (distance * Math.Max(4 - distance, 1))).magnitude));
                    flightCamera.transform.parent.position -= positionDifference.normalized * Time.fixedDeltaTime * (distance * Math.Max(4 - distance, 1));
                    // if the parts are not of the same craft, boost the speed at which we move towards it
                    Part part = targetTransform != null ? Part.FromGO(targetTransform.gameObject) : null;
                    if ((part != null && part.vessel != vessel) || targetTransform == null)
                    {
                        flightCamera.transform.parent.position -= positionDifference.normalized * Time.fixedDeltaTime;
                        if (Time.time - startFocusTime > 10.0f)
                        {
                            hasReachedTarget = true;
                            DebugPrint("UpdateFocus 2");
                        }
                        else
                            DebugPrint("UpdateFocus 3");
                    }
                    else
                        DebugPrint("UpdateFocus 4");
                }

            }
        }

        Transform GetTransform()
        {
            Vector3 aim = new Vector3(Input.mousePosition.x, Input.mousePosition.y, 0);
            Ray ray = flightCamera.mainCamera.ScreenPointToRay(aim);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 10000, 0x80001))
            {
                return hit.transform;
            }
            return null;
        }
    }

    static public class API
    {
        static private CameraFocusChanger s_cfcInstance = null;

        static public void SetInstance(CameraFocusChanger cfcInstance)
        {
            s_cfcInstance = cfcInstance;
        }

        static public bool IsCFCAvailable()
        {
            return s_cfcInstance != null;
        }

        static public bool FocusOnPart(Part part)
        {
            if (s_cfcInstance != null)
            {
                if (part != null)
                {
                    s_cfcInstance.FocusOn(part.transform);
                }
                else
                {
                    s_cfcInstance.ResetFocus();
                }
                return true;
            }

            return false;
        }

        static public bool ResetFocus()
        {
            if (s_cfcInstance != null)
            {
                s_cfcInstance.ResetFocus();
                return true;
            }

            return false;
        }
    }
}
