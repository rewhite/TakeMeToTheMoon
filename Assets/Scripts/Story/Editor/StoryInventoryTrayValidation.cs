using System;
using System.Collections;
using System.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;

namespace TakeMeToTheMoon.Story.Editor
{
    /// <summary>Exercises real Meta release/snap/undock events without a headset. Play Mode only.</summary>
    public static class StoryInventoryTrayValidation
    {
        private static IEnumerator run;
        private static double nextTick;

        [MenuItem("Tools/Story/Validate Inventory Tray (Play Mode)")]
        public static void Validate()
        {
            if (!EditorApplication.isPlaying || run != null) { Debug.LogWarning("Enter Play Mode first."); return; }
            run = Check();
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextTick) return;
            nextTick = EditorApplication.timeSinceStartup + 0.15;
            try
            {
                if (EditorApplication.isPlaying && run.MoveNext()) return;
            }
            catch (Exception ex) { Debug.LogException(ex); }
            (run as IDisposable)?.Dispose();
            run = null;
            EditorApplication.update -= Tick;
        }
        private static IEnumerator Check()
        {
            var tray = UnityEngine.Object.FindFirstObjectByType<StoryInventoryTray>(FindObjectsInactive.Include);
            Require(tray != null, "Configured tray exists");
            var data = new SerializedObject(tray);
            var controller = (StoryController)data.FindProperty("controller").objectReferenceValue;
            var array = data.FindProperty("allowedItems");
            var items = Enumerable.Range(0, array.arraySize).Select(i => (SnapInteractor)array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            var adapter = controller.GetComponent<StorySocketSelection>();
            var slotObjects = tray.Slots;
            Require(slotObjects != null && slotObjects.Length == 3, "Three independent socket objects");
            Require(tray.GetComponent<SnapInteractable>() == null && tray.GetComponent<BoxCollider>() == null, "Shared socket and collider removed");
            var socket = slotObjects[0].Socket;
            var ghost = tray.GetComponent<StoryInventoryGhost>();
            var placeholders = tray.GetComponent<StoryInventoryPlaceholders>();
            int dockSounds = 0;
            UnityEngine.Events.UnityAction countDock = () => dockSounds++;
            tray.ItemDocked.AddListener(countDock);
            controller.ResetStory();
            if (tray.transform.parent != null && tray.transform.parent.name == StoryInventoryGroupSetup.GroupName)
                tray.transform.parent.gameObject.SetActive(true);
            tray.gameObject.SetActive(true);
            foreach (var item in items) item.gameObject.SetActive(true);
            yield return null;
            yield return null;
            if (placeholders != null) Require(placeholders.VisibleCount == 3, "Three empty placeholders appear");
            var displayMotion = items[0].GetComponent<StoryItemDisplayMotion>();
            if (displayMotion != null)
            {
                Quaternion before = items[0].transform.rotation;
                yield return null; yield return null;
                Require(Quaternion.Angle(before, items[0].transform.rotation) > .1f, "Idle display rotates");
            }
            Require(!tray.SnapPoseForElement(int.MinValue, new Pose(tray.transform.position, Quaternion.identity), out _), "Unknown object rejected");
            for (int i = 0; i < 4; i++)
            {
                var item = items[i];
                int destinationIndex = new[] { 2, 0, 1, 2 }[i];
                socket = slotObjects[destinationIndex].Socket;
                Grab(item, 100000 + i);
                var grabSource = item.transform.Find("Grab Audio")?.GetComponent<AudioSource>();
                if (grabSource != null) Require(grabSource.isPlaying, "Grab starts configured audio");
                Move(item, 100000 + i, socket.transform.TransformPoint(new Vector3(0, .1f, 0)) - item.SnapPose.position);
                item.Rigidbody.position = item.transform.position;
                Physics.SyncTransforms();
                yield return null;
                yield return null;
                Require(item.State != InteractorState.Select, "Held item does not snap");
                var motion = item.GetComponent<StoryItemDisplayMotion>();
                if (motion != null) Require(motion.IsHeld, "Display motion pauses for external grab");
                if (i < 3) Require(item.Interactable == socket, "Held item finds tray: state=" + item.State + " anchor=" + item.SnapPose.position + " target=" + tray.transform.position);
                if (ghost != null)
                {
                    Require(ghost.PreviewedItem == (i < 3 ? item : null), "Ghost only shows an accepted held item");
                    if (i < 3)
                    {
                        socket.PoseForInteractor(item, out Pose target);
                        Require(ghost.PreviewedSlot == destinationIndex, "Ghost matches the targeted socket, not first free slot");
                        Vector3 anchorInRoot = item.transform.InverseTransformPoint(item.SnapPose.position);
                        Require(Vector3.Distance(ghost.VisibleGhost.TransformPoint(anchorInRoot), target.position) < .001f,
                            "Ghost bottom anchor matches actual destination");
                        Require(ghost.VisibleGhost.GetComponentsInChildren<Collider>(true).Length == 0,
                            "Ghost does not block rays or physics");
                        Require(ghost.VisibleGhost.GetComponentsInChildren<MonoBehaviour>(true).Length == 0,
                            "Ghost has no interaction or audio scripts");
                    }
                }
                if (placeholders != null) Require(placeholders.VisibleCount == (i < 3 ? 2 - i : 0), "Ghost replaces only its empty placeholder");
                Release(item, 100000 + i);
                yield return null;
                yield return null;
                adapter.RefreshSelection();
                if (ghost != null) Require(ghost.VisibleGhost == null, "Ghost disappears on release");
                if (placeholders != null) Require(placeholders.VisibleCount == 3 - Mathf.Min(i + 1, 3), "Occupied positions hide placeholders");
                Require(controller.Inventory.Count == Mathf.Min(i + 1, 3), "Release fills a free position, capacity three: " + i);
                if (i == 0)
                {
                    var occupiedPose = new Pose(slotObjects[2].transform.TransformPoint(new Vector3(0, .1f, 0)), Quaternion.identity);
                    Require(!tray.SnapPoseForElement(items[4].Identifier, occupiedPose, out _), "Occupied target rejected despite other empty slots");
                    Vector3 gap = (slotObjects[0].transform.position + slotObjects[1].transform.position) * .5f + tray.transform.up * .1f;
                    Require(!tray.SnapPoseForElement(items[4].Identifier, new Pose(gap, Quaternion.identity), out _), "Gap between socket colliders rejects drop");
                }
            }
            if (displayMotion != null)
            {
                items[0].Interactable.PoseForInteractor(items[0], out Pose bobBefore);
                yield return null; yield return null;
                items[0].Interactable.PoseForInteractor(items[0], out Pose bobAfter);
                Require(Vector3.Distance(bobBefore.position, bobAfter.position) > .00001f, "Docked destination bobs");
                Require(Quaternion.Angle(bobBefore.rotation, bobAfter.rotation) < .01f, "Docked display stops spinning");
            }
            Require(dockSounds == 3, "Only accepted items emit dock sound; full tray stays silent");
            Require(slotObjects.Sum(slot => slot.Socket.SelectingInteractors.Count) == 3, "Three physical selections");
            var poses = items.Take(3).Select(item => { item.Interactable.PoseForInteractor(item, out Pose pose); return pose.position; }).ToArray();
            Require(poses.Distinct().Count() == 3, "Selected items have distinct positions");
            Grab(items[1], 200001);
            yield return null;
            adapter.RefreshSelection();
            Require(controller.Inventory.Count == 2, "Re-grab frees logical inventory");
            Move(items[1], 200001, Vector3.up * 2);
            items[1].Rigidbody.position = items[1].transform.position;
            Physics.SyncTransforms();
            yield return null;
            Release(items[1], 200001);
            yield return null;
            Require(controller.Inventory.Count == 2, "Release outside does not snap");
            if (placeholders != null) Require(placeholders.VisibleCount == 1, "Removing an item restores its placeholder");
            if (ghost != null) Require(ghost.VisibleGhost == null, "No ghost outside drop zone");
            var returnMotion = items[1].GetComponent<StoryItemDisplayMotion>();
            if (returnMotion != null && returnMotion.Home != null)
            {
                float deadline = Time.time + 3;
                while (Vector3.Distance(items[1].transform.position, returnMotion.Home.position) > .005f && Time.time < deadline)
                    yield return null;
                Require(Vector3.Distance(items[1].transform.position, returnMotion.Home.position) < .005f, "Undocked item returns home after outside release");
                Require(controller.Inventory.Count == 2, "Home positions do not count as inventory");
            }
            socket = slotObjects[0].Socket;
            Grab(items[3], 200003);
            Move(items[3], 200003, socket.transform.TransformPoint(new Vector3(0, .1f, 0)) - items[3].SnapPose.position);
            items[3].Rigidbody.position = items[3].transform.position;
            Physics.SyncTransforms();
            yield return null;
            Release(items[3], 200003);
            yield return null;
            yield return null;
            adapter.RefreshSelection();
            Require(controller.Inventory.Count == 3 && controller.CanStart, "Replacement fills freed position");
            Require(dockSounds == 4, "Replacement emits exactly one dock sound");
            tray.ItemDocked.RemoveListener(countDock);
            tray.gameObject.SetActive(false);
            yield return null;
            adapter.RefreshSelection();
            Require(controller.Inventory.Count == 0, "Hiding tray clears pre-story selection");
            if (ghost != null) Require(ghost.VisibleGhost == null, "Hiding tray clears preview");
            if (placeholders != null) Require(placeholders.VisibleCount == 0, "Hiding tray clears placeholders");
            // Test actual Meta ray hit testing and movement, using a virtual select input.
            if (items[4].GetComponentInChildren<RayInteractable>() != null)
            {
                tray.gameObject.SetActive(true);
                socket = slotObjects[1].Socket;
                var prop = items[4];
                var rayTarget = prop.GetComponentInChildren<RayInteractable>();
                foreach (var item in items)
                {
                    Require(!item.GetComponent<GrabInteractable>().enabled, "Direct controller grab disabled");
                    Require(!item.GetComponent<Oculus.Interaction.HandGrab.HandGrabInteractable>().enabled, "Direct hand grab disabled");
                    Require(item.GetComponentInChildren<RayInteractable>() != null, "Ray interaction present");
                }
                // Isolate the test ray from other props now returning to their display positions.
                foreach (var item in items) if (item != prop) item.gameObject.SetActive(false);
                var propMotion = prop.GetComponent<StoryItemDisplayMotion>();
                if (propMotion != null && propMotion.Home != null) propMotion.Home.position = new Vector3(20, 20, 20);
                prop.transform.position = new Vector3(20, 20, 20);
                prop.Rigidbody.position = prop.transform.position;
                Physics.SyncTransforms();
                var driver = new GameObject("Inventory validation ray (temporary)");
                driver.SetActive(false);
                var selector = driver.AddComponent<VirtualSelector>();
                var ray = driver.AddComponent<RayInteractor>();
                ray.InjectAllRayInteractor(selector, driver.transform);
                driver.transform.position = prop.GetComponent<Collider>().bounds.center - Vector3.forward;
                driver.transform.rotation = Quaternion.identity;
                driver.SetActive(true);
                try
                {
                    yield return null;
                    yield return null;
                    Require(ray.Interactable == rayTarget, "Ray hits distant item collider");
                    selector.Select();
                    yield return null;
                    Require(ray.State == InteractorState.Select, "Ray selects item");
                    driver.transform.position += socket.transform.TransformPoint(new Vector3(0, .1f, 0)) - prop.SnapPose.position;
                    yield return null;
                    yield return null;
                    Require(prop.State != InteractorState.Select, "Ray-held item stays unsnapped");
                    Require(prop.Interactable == socket, "Ray carries item into tray zone");
                    if (ghost != null) Require(ghost.PreviewedItem == prop, "Actual ray drag displays ghost");
                    selector.Unselect();
                    yield return null;
                    yield return null;
                    adapter.RefreshSelection();
                    Require(controller.Inventory.Count == 1, "Ray release docks into inventory");
                    // Aim again at its new snapped location, then remove it using the ray.
                    driver.transform.position = prop.GetComponent<Collider>().bounds.center - Vector3.forward;
                    yield return null;
                    yield return null;
                    Require(ray.Interactable == rayTarget, "Ray can target docked item");
                    selector.Select();
                    yield return null;
                    adapter.RefreshSelection();
                    Require(controller.Inventory.Count == 0, "Ray re-grab removes inventory item");
                    selector.Unselect();
                }
                finally { UnityEngine.Object.Destroy(driver); }
            }
            Debug.Log("INVENTORY TRAY VALIDATION PASSED: real Meta hold/release, three distinct poses, capacity, unknown rejection, undock, outside release, replacement, disable cleanup, configured ray hit/drag/drop/re-grab, ghost placement and visibility, empty-slot placeholder visibility, idle rotation, dock bob, return home and three independently targeted sockets. Exit Play Mode to restore scene.");
        }
        private static void Move(SnapInteractor item, int id, Vector3 delta) => ((PointableElement)item.PointableElement).ProcessPointerEvent(new PointerEvent(id, PointerEventType.Move, new Pose(item.transform.position + delta, item.transform.rotation)));
        private static void Grab(SnapInteractor item, int id)
        {
            var pointable = (PointableElement)item.PointableElement;
            var pose = new Pose(item.transform.position, item.transform.rotation);
            pointable.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Hover, pose));
            pointable.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Select, pose));
        }
        private static void Release(SnapInteractor item, int id)
        {
            var pointable = (PointableElement)item.PointableElement;
            var pose = new Pose(item.transform.position, item.transform.rotation);
            pointable.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Unselect, pose));
            pointable.ProcessPointerEvent(new PointerEvent(id, PointerEventType.Unhover, pose));
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception("Inventory validation failed: " + message); }
    }
}
