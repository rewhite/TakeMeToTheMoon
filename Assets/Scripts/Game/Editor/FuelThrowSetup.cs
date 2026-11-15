using System.Linq;
using Oculus.Interaction;
using TakeMeToTheMoon.Story;
using TakeMeToTheMoon.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class FuelThrowSetup
    {
        [MenuItem("Tools/Game Flow/Configure Stage 3 Fuel Minigame")]
        public static void Configure()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first");
            var manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            var story = Object.FindAnyObjectByType<StoryController>(FindObjectsInactive.Include);
            var data = new SerializedObject(manager);
            var stage = data.FindProperty("stages").GetArrayElementAtIndex(2);
            var stageRoot = stage.FindPropertyRelative("screenPrefab").objectReferenceValue as GameObject;
            if (stageRoot == null || !stageRoot.scene.IsValid()) throw new System.Exception("Preplaced Stage 3 root required");
            var existing = stageRoot.GetComponentInChildren<FuelThrowGame>(true);
            if (existing != null)
            {
                Repair(existing); EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
                EditorSceneManager.SaveScene(existing.gameObject.scene);
                Selection.activeGameObject = existing.gameObject; return;
            }
            var originals = Object.FindObjectsByType<StoryItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(i => i.GetComponent<StoryItemDisplayMotion>() != null).OrderBy(i => i.name).ToArray();
            if (originals.Length == 0) throw new System.Exception("Inventory props required");
            var eye = manager.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.name == "CenterEyeAnchor");
            var root = new GameObject("Stage 3 Fuel Minigame");
            Undo.RegisterCreatedObjectUndo(root, "Create fuel minigame");
            root.transform.SetParent(stageRoot.transform, false);
            root.transform.SetPositionAndRotation(eye.transform.position, Quaternion.Euler(0, eye.transform.eulerAngles.y, 0));
            var game = root.AddComponent<FuelThrowGame>(); game.manager = manager; game.story = story;
            game.audioSource = root.AddComponent<AudioSource>(); game.audioSource.playOnAwake = false; game.audioSource.spatialBlend = 0;
            game.startSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audios/throw-gamestart.ogg");
            game.hitSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audios/throw-success.ogg");
            game.missSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audios/throw-fail.ogg");
            var targetMat = Material("Fuel Target Orange", new Color(.9f, .22f, .045f));
            var trayMat = Material("Fuel Tray Black", new Color(.025f, .03f, .03f));
            game.targets = new FuelPhysicsTarget[3]; game.itemPositions = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * .7f;
                var home = Child(root.transform, "Target " + (i + 1) + " Home", new Vector3(x, .3f, 2.5f));
                var cube = Primitive(root.transform, "Target " + (i + 1), PrimitiveType.Cube,
                    home.localPosition, new Vector3(.34f, .52f, .09f), targetMat);
                var body = cube.AddComponent<Rigidbody>(); body.mass = .2f; body.linearDamping = .05f; body.angularDamping = .08f;
                body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var target = cube.AddComponent<FuelPhysicsTarget>(); target.game = game; target.body = body; target.home = home;
                game.targets[i] = target;
                // Invisible support: no renderer or visual floor is added.
                var support = Child(root.transform, "Invisible Support " + (i + 1), new Vector3(x, .02f, 2.5f));
                var collider = support.gameObject.AddComponent<BoxCollider>(); collider.size = new Vector3(.6f, .04f, .8f);
                game.itemPositions[i] = Child(root.transform, "Item Position " + (i + 1), new Vector3((i - 1) * .25f, -.43f, .95f));
            }
            var tray = Primitive(root.transform, "Inventory Tray", PrimitiveType.Cylinder, new Vector3(0, -.57f, .95f), new Vector3(.9f, .012f, .4f), trayMat);
            Object.DestroyImmediate(tray.GetComponent<Collider>());
            game.items = new FuelThrowItem[originals.Length + 1];
            for (int i = 0; i < game.items.Length; i++)
            {
                bool fallback = i == originals.Length;
                var source = originals[fallback ? 0 : i];
                var clone = Object.Instantiate(source.gameObject, root.transform);
                clone.name = fallback ? "Spare Throw Prop (empty inventory)" : "Throw " + source.name;
                foreach (var motion in clone.GetComponentsInChildren<StoryItemDisplayMotion>(true)) motion.enabled = false;
                foreach (var snap in clone.GetComponentsInChildren<SnapInteractor>(true)) snap.enabled = false;
                var item = clone.AddComponent<FuelThrowItem>(); item.game = game;
                item.definition = fallback ? null : source.Definition;
                item.body = clone.GetComponent<Rigidbody>(); item.body.mass = .35f;
                item.body.isKinematic = true; item.body.useGravity = true;
                item.body.interpolation = RigidbodyInterpolation.Interpolate;
                item.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                item.grabbable = clone.GetComponent<Grabbable>();
                item.grabbable.InjectOptionalRigidbody(item.body);
                item.grabbable.InjectOptionalThrowWhenUnselected(true);
                item.grabbable.ForceKinematicDisabled = true;
                item.rays = clone.GetComponentsInChildren<RayInteractable>(true);
                item.home = game.itemPositions[i % 3];
                clone.transform.SetPositionAndRotation(item.home.position, item.home.rotation);
                game.items[i] = item;
                clone.SetActive(!fallback && i < 3);
            }
            var panel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fuel Instruction Panel.prefab"), root.transform);
            panel.name = "Fuel Instructions";
            var rect = (RectTransform)panel.transform; rect.localPosition = new Vector3(0, -.22f, 1.65f); rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * .0007f; rect.sizeDelta = new Vector2(1180, 850);
            var canvas = panel.GetComponent<Canvas>(); canvas.worldCamera = eye;
            game.instructions = panel.GetComponent<FuelInstructionPanel>();
            var result = new GameObject("Throw Result", typeof(RectTransform), typeof(TextMeshProUGUI));
            result.transform.SetParent(panel.transform, false);
            var text = result.GetComponent<TextMeshProUGUI>(); text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset");
            text.fontSize = 30; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(1100, 60); text.rectTransform.anchoredPosition = new Vector2(0, -245);
            text.text = "Throw an item at a target"; game.resultText = text;
            var buttonObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Primary Button.prefab"), panel.transform);
            ((RectTransform)buttonObject.transform).anchoredPosition = new Vector2(0, -335);
            buttonObject.GetComponentInChildren<TMP_Text>().text = "CONTINUE";
            game.continueButton = buttonObject.GetComponent<Button>();
            var interactionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("8369d93f7b6b99742bbea0649a41b7b1"));
            var interaction = Object.Instantiate(interactionPrefab, panel.transform, false);
            interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
            var interactionRect = (RectTransform)interaction.transform;
            interactionRect.anchorMin = Vector2.zero; interactionRect.anchorMax = Vector2.one;
            interactionRect.offsetMin = interactionRect.offsetMax = Vector2.zero;
            foreach (var component in panel.GetComponentsInChildren<Component>(true))
                if (component != null && PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            stage.FindPropertyRelative("revealWhen").enumValueIndex = (int)RevealCondition.VideoEnded;
            var targets = stage.FindPropertyRelative("revealObjects"); int index = targets.arraySize++;
            targets.GetArrayElementAtIndex(index).objectReferenceValue = root;
            // Keep the user's clip if assigned; otherwise use the existing stage 1 demo clip for testing.
            var clip = stage.FindPropertyRelative("videoClip");
            if (clip.objectReferenceValue == null && string.IsNullOrWhiteSpace(stage.FindPropertyRelative("videoUrl").stringValue))
                clip.objectReferenceValue = data.FindProperty("stages").GetArrayElementAtIndex(0).FindPropertyRelative("videoClip").objectReferenceValue;
            data.ApplyModifiedProperties();
            Repair(game);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Selection.activeGameObject = root;
            Debug.Log("FUEL SETUP PASSED: preplaced stage 3 game, 3 physics cube targets, invisible supports, Meta ray throws, carried props, panel, Continue, VideoEnded reveal.");
        }
        private static void Repair(FuelThrowGame game)
        {
            game.resultFlash = Object.FindAnyObjectByType<PrologueEmergencyPulse>(FindObjectsInactive.Include);
            // Homes follow the authored tray, including later position/scale edits.
            var tray = game.transform.Find("Inventory Tray");
            var mesh = tray.GetComponent<MeshFilter>().sharedMesh.bounds;
            for (int i = 0; i < game.itemPositions.Length; i++)
            {
                var home = game.itemPositions[i];
                home.SetParent(tray, false);
                home.localPosition = new Vector3(mesh.center.x + (i - 1) * mesh.size.x * .28f,
                    mesh.max.y + .015f / Mathf.Max(.001f, Mathf.Abs(tray.lossyScale.y)), mesh.center.z);
                home.localRotation = Quaternion.identity;
            }
            game.flightTimeout = 6f;
            game.hitPoints = game.missPoints = 5;
            var panel = game.instructions.gameObject;
            if (game.resultToast == null)
            {
                var toastObject = new GameObject("Wellness Result Toast", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(RawImage), typeof(FuelResultToast));
                toastObject.transform.SetParent(panel.transform, false);
                var rect = (RectTransform)toastObject.transform;
                rect.anchoredPosition = new Vector2(0, -240);
                rect.sizeDelta = new Vector2(700, 176);
                var canvas = toastObject.GetComponent<Canvas>();
                canvas.overrideSorting = true; canvas.sortingOrder = 30;
                var toast = toastObject.GetComponent<FuelResultToast>();
                toast.image = toastObject.GetComponent<RawImage>(); toast.image.raycastTarget = false;
                toast.group = toastObject.GetComponent<CanvasGroup>();
                toast.group.blocksRaycasts = false; toast.group.interactable = false;
                toast.successImage = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Images/Wellness Change_Popup_success.png");
                toast.failureImage = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Images/Wellness Change_Popup_fail.png");
                toast.image.texture = toast.successImage;
                toast.Clear(); game.resultToast = toast;
            }
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
            foreach (var item in game.items)
            {
                foreach (var ray in item.rays)
                {
                    var pull = ray.GetComponent<PullToHandProvider>();
                    if (pull == null) pull = ray.gameObject.AddComponent<PullToHandProvider>();
                    ray.InjectOptionalMovementProvider(pull);
                }
                item.throwSpeedMultiplier = 2.8f;
                item.gravityScale = .18f;
                item.minimumThrowSpeed = .15f;
                item.minimumLaunchSpeed = 3.8f;
                item.upwardAssist = 1.1f;
                item.body.linearDamping = 0;
                item.body.constraints = RigidbodyConstraints.None;
                item.body.detectCollisions = true;
                item.gameObject.layer = 0;
                // Keep the authored ray colliders, but use one consistent physical throw shape.
                foreach (var collider in item.GetComponentsInChildren<Collider>(true)) collider.isTrigger = true;
                var solid = item.GetComponent<SphereCollider>();
                if (solid == null) solid = item.gameObject.AddComponent<SphereCollider>();
                solid.isTrigger = false;
                solid.center = Vector3.zero;
                var scale = item.transform.lossyScale;
                solid.radius = .08f / Mathf.Max(.001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            }
            foreach (var item in game.items) item.ApplyHomePose();
            Debug.Log("Fuel throw repair applied: UI raycaster and consistent solid projectile colliders.");
        }
        private static Transform Child(Transform parent, string name, Vector3 position)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go.transform; }
        private static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        private static Material Material(string name, Color color)
        {
            string path = "Assets/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", color); AssetDatabase.CreateAsset(material, path); }
            return material;
        }
    }
}
