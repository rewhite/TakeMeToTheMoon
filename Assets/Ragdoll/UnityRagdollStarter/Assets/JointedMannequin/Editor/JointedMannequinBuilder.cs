#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;

namespace JointedMannequin.EditorTools
{
    /// <summary>
    /// Builds the physics prefab from the supplied, segmented FBX. The model is
    /// intentionally a set of rigid meshes; it does not require an Animator.
    /// </summary>
    public static class JointedMannequinBuilder
    {
        private const string RootPath = "Assets/Ragdoll/UnityRagdollStarter/Assets/JointedMannequin";
        private const string ModelPath = RootPath + "/Models/JointedMannequin.fbx";
        private const string PrefabPath = RootPath + "/Prefabs/JointedMannequin.prefab";

        [MenuItem("Tools/Jointed Mannequin/Add Quest Ray Ragdoll to Scene")]
        public static void AddQuestRagdoll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            GameObject prefab = BuildPrefab();
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Add Quest ray ragdoll");
            Selection.activeGameObject = instance;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(Vector3.up, Vector3.one * 2), false);
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Debug.Log("Quest ray ragdoll added. Position it in the scene; select any body part to hold its head.", instance);
        }

        private sealed class Part
        {
            public string Name;
            public Vector3 Center;
            public Vector3 Size;
            public float Mass;
            public bool Capsule;
            public Vector3 CapsuleUp = Vector3.up;
            public string ConnectedTo;
            public Vector3 JointPosition;
            public float Low, High, Swing1, Swing2;

            public Part(string name, Vector3 center, Vector3 size, float mass,
                        bool capsule = false, Vector3? capsuleUp = null)
            {
                Name = name;
                Center = center;
                Size = size;
                Mass = mass;
                Capsule = capsule;
                if (capsuleUp.HasValue) CapsuleUp = capsuleUp.Value.normalized;
            }

            public Part Connect(string parent, Vector3 position,
                                float low, float high, float swing1, float swing2)
            {
                ConnectedTo = parent;
                JointPosition = position;
                Low = low;
                High = high;
                Swing1 = swing1;
                Swing2 = swing2;
                return this;
            }
        }

        [MenuItem("Tools/Jointed Mannequin/Create Ragdoll Prefab")]
        public static void CreateRagdollPrefab()
        {
            try
            {
                GameObject prefab = BuildPrefab();
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log("Created " + AssetDatabase.GetAssetPath(prefab) +
                          ". Drag it into your scene, or use Tools > Jointed Mannequin > Create Demo Scene.", prefab);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Jointed mannequin", exception.Message, "OK");
            }
        }

        [MenuItem("Tools/Jointed Mannequin/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            // The normal Unity save prompt protects any open, modified scenes.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null) prefab = BuildPrefab();

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                character.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(character, "Create mannequin demo");

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Floor";
                floor.transform.localScale = new Vector3(1.4f, 1f, 1.4f);
                floor.GetComponent<Renderer>().sharedMaterial = GetMaterial("DemoFloor", new Color(0.23f, 0.29f, 0.34f, 1f));

                GameObject cameraObject = new GameObject("Main Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<AudioListener>();
                camera.transform.position = new Vector3(2.25f, 1.55f, 3.35f);
                camera.transform.LookAt(new Vector3(0f, 0.92f, 0f));
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 50f;
                camera.fieldOfView = 43f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.065f, 0.085f, 0.12f, 1f);

                GameObject lightObject = new GameObject("Key Light");
                Light keyLight = lightObject.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.intensity = 1.3f;
                keyLight.color = new Color(1f, 0.94f, 0.85f, 1f);
                keyLight.shadows = LightShadows.Soft;
                lightObject.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.59f, 0.67f, 1f);

                JointedRagdollMouseDemo mouseDemo = new GameObject("Mouse Drag Demo").AddComponent<JointedRagdollMouseDemo>();
                mouseDemo.Configure(character.GetComponent<JointedRagdoll>(), camera);

                EnsureFolder(RootPath + "/Demo");
                string scenePath = AssetDatabase.GenerateUniqueAssetPath(RootPath + "/Demo/JointedMannequinDemo.unity");
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                    throw new InvalidOperationException("The demo is open but could not be saved. Save it with File > Save As.");
                Selection.activeObject = character;
                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(1.3f, 2.1f, 1.3f)), false);
                Debug.Log("Created " + scenePath + ". Press Play and drag a body part with the left mouse button. " +
                          "Mouse controls require Active Input Handling = Input Manager (Old) or Both; " +
                          "XR uses the toolkit-independent JointedRagdoll BeginGrab/EndGrab methods.", character);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Jointed mannequin", exception.Message, "OK");
            }
        }

        private static GameObject BuildPrefab()
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
                throw new InvalidOperationException("Put the included JointedMannequin folder inside your project's Assets folder. " +
                                                    "Expected model: " + ModelPath);

            Part[] parts = GetParts();
            GameObject source = null;
            GameObject root = null;
            try
            {
                source = UnityEngine.Object.Instantiate(modelAsset);
                source.name = "Temporary imported mannequin";
                source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var visualParts = new Dictionary<string, Transform>();
                foreach (Part part in parts)
                {
                    Transform visual = FindNamed(source.transform, part.Name);
                    if (visual == null)
                        throw new InvalidOperationException("The FBX is missing body section '" + part.Name + "'. " +
                                                            "Keep the model's separate named parts when exporting.");
                    visualParts.Add(part.Name, visual);
                }
                NormalizeImportedModel(source.transform, visualParts);

                root = new GameObject("JointedMannequin");
                var bodies = new Dictionary<string, Rigidbody>();
                var orderedBodies = new List<Rigidbody>();
                foreach (Part part in parts)
                {
                    Transform visual = visualParts[part.Name];
                    if (Vector3.Distance(visual.position, part.Center) > 0.02f)
                        throw new InvalidOperationException("Imported section '" + part.Name + "' at " + visual.position + " expected " + part.Center + ". " +
                                                            "Use the included FBX, with its original object pivots and proportions.");

                    GameObject bodyObject = new GameObject(part.Name);
                    bodyObject.transform.SetParent(root.transform, false);
                    bodyObject.transform.localPosition = part.Center;
                    visual.SetParent(bodyObject.transform, true);
                    visual.name = "Visual";

                    // A compound collider may rotate with a slanted limb, while
                    // the Rigidbody itself keeps an identity rotation and scale.
                    if (part.Capsule)
                    {
                        GameObject collisionObject = new GameObject("Collision");
                        collisionObject.transform.SetParent(bodyObject.transform, false);
                        collisionObject.transform.localRotation = Quaternion.FromToRotation(Vector3.up, part.CapsuleUp);
                        CapsuleCollider collider = collisionObject.AddComponent<CapsuleCollider>();
                        collider.direction = 1;
                        collider.radius = Mathf.Max(part.Size.x, part.Size.z) * 0.5f;
                        collider.height = Mathf.Max(part.Size.y, collider.radius * 2f);
                    }
                    else
                    {
                        BoxCollider collider = bodyObject.AddComponent<BoxCollider>();
                        collider.size = part.Size;
                    }

                    Rigidbody body = bodyObject.AddComponent<Rigidbody>();
                    body.mass = part.Mass;
                    body.useGravity = true;
                    body.isKinematic = true;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.solverIterations = 12;
                    body.solverVelocityIterations = 4;
                    body.maxAngularVelocity = 20f;
#if UNITY_6000_0_OR_NEWER
                    body.linearDamping = 0.05f;
                    body.angularDamping = 0.12f;
#else
                    body.drag = 0.05f;
                    body.angularDrag = 0.12f;
#endif
                    bodies.Add(part.Name, body);
                    orderedBodies.Add(body);
                }

                foreach (Part part in parts)
                {
                    if (string.IsNullOrEmpty(part.ConnectedTo)) continue;
                    Rigidbody body = bodies[part.Name];
                    Rigidbody connected = bodies[part.ConnectedTo];
                    CharacterJoint joint = body.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = connected;
                    joint.autoConfigureConnectedAnchor = false;
                    joint.anchor = body.transform.InverseTransformPoint(part.JointPosition);
                    joint.connectedAnchor = connected.transform.InverseTransformPoint(part.JointPosition);
                    joint.axis = Vector3.right;
                    joint.swingAxis = Vector3.up;
                    joint.lowTwistLimit = MakeLimit(part.Low);
                    joint.highTwistLimit = MakeLimit(part.High);
                    joint.swing1Limit = MakeLimit(part.Swing1);
                    joint.swing2Limit = MakeLimit(part.Swing2);
                    joint.enableCollision = false;
                    joint.enableProjection = true;
                    joint.projectionDistance = 0.025f;
                    joint.projectionAngle = 12f;
                    joint.breakForce = Mathf.Infinity;
                    joint.breakTorque = Mathf.Infinity;
                }

                RemapMaterials(root);
                JointedRagdoll ragdoll = root.AddComponent<JointedRagdoll>();
                ragdoll.Configure(orderedBodies.ToArray(), bodies["Chest"]);
                ConfigureQuestRay(root, ragdoll, bodies["Head"]);
                EnsureFolder(RootPath + "/Prefabs");
                // Preserve a user's previously generated or edited prefab.
                string outputPath = AssetDatabase.GenerateUniqueAssetPath(PrefabPath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                if (saved == null) throw new InvalidOperationException("Unity could not save the generated ragdoll prefab.");
                AssetDatabase.SaveAssets();
                return saved;
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static void ConfigureQuestRay(GameObject root, JointedRagdoll ragdoll, Rigidbody head)
        {
            var transformer = root.AddComponent<JointedRagdollRayGrab>();
            transformer.Configure(ragdoll, head);
            var grabbable = root.AddComponent<Grabbable>();
            grabbable.MaxGrabPoints = 1;
            grabbable.InjectOptionalOneGrabTransformer(transformer);
            grabbable.InjectOptionalThrowWhenUnselected(false);
            grabbable.InjectOptionalKinematicWhileSelected(false);
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                var target = new GameObject("Quest Ray Grab");
                target.transform.SetParent(collider.transform, false);
                var surface = target.AddComponent<ColliderSurface>();
                var serialized = new SerializedObject(surface);
                serialized.FindProperty("_collider").objectReferenceValue = collider;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var ray = target.AddComponent<RayInteractable>();
                ray.InjectAllRayInteractable(surface);
                ray.InjectOptionalMovementProvider(transformer);
                ray.InjectOptionalPointableElement(grabbable);
            }
        }

        private static void NormalizeImportedModel(Transform source, Dictionary<string, Transform> parts)
        {
            Vector3 up = parts["Head"].position - parts["Pelvis"].position;
            Vector3 right = parts["UpperArm_R"].position - parts["UpperArm_L"].position;
            if (up.sqrMagnitude < 0.000001f || right.sqrMagnitude < 0.000001f)
                throw new InvalidOperationException("The FBX section origins are invalid. Re-export the included model without merging its parts.");
            float uniformScale = 0.65f / up.magnitude;
            up.Normalize();
            right = Vector3.ProjectOnPlane(right, up).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            source.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * source.rotation;
            source.localScale *= uniformScale;
            source.position += new Vector3(0f, 0.975f, 0f) - parts["Pelvis"].position;
            // FBX handedness conversion can mirror depth while preserving named left/right parts.
            // Reflect the visuals only; generated physics bodies retain positive unit scale.
            if (parts["Foot_L"].position.z < parts["Pelvis"].position.z)
            {
                source.localScale = Vector3.Scale(source.localScale, new Vector3(1f, 1f, -1f));
                source.position += new Vector3(0f, 0.975f, 0f) - parts["Pelvis"].position;
            }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == name) return candidate;
            return null;
        }

        private static SoftJointLimit MakeLimit(float angle)
        {
            return new SoftJointLimit { limit = angle, bounciness = 0f, contactDistance = 0f };
        }

        private static void RemapMaterials(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    string name = source != null ? source.name : "Porcelain";
                    if (name.IndexOf("Apricot", StringComparison.OrdinalIgnoreCase) >= 0)
                        materials[index] = GetMaterial("Apricot", new Color(0.96f, 0.45f, 0.13f, 1f));
                    else if (name.IndexOf("Visor", StringComparison.OrdinalIgnoreCase) >= 0)
                        materials[index] = GetMaterial("Visor", new Color(0.025f, 0.055f, 0.075f, 1f));
                    else if (name.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0)
                        materials[index] = GetMaterial("Highlight", new Color(0.68f, 0.89f, 1f, 1f));
                    else
                        materials[index] = GetMaterial("Porcelain", new Color(0.88f, 0.91f, 0.94f, 1f));
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material GetMaterial(string name, Color color)
        {
            EnsureFolder(RootPath + "/Materials");
            Shader shader = FindLitShader();
            string path = RootPath + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null && material.shader == shader) return material;
            // Different render pipelines get their own compatible material.
            if (material != null) path = AssetDatabase.GenerateUniqueAssetPath(path);
            material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.04f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.35f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.35f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Shader FindLitShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string shaderName = "Standard";
            if (pipeline != null)
            {
                string pipelineType = pipeline.GetType().Name;
                if (pipelineType.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                    shaderName = "Universal Render Pipeline/Lit";
                else if (pipelineType.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0)
                    shaderName = "HDRP/Lit";
                else
                    throw new InvalidOperationException("This builder supports Unity's Built-in, Universal, and High Definition render pipelines. " +
                                                        "For a custom pipeline, assign its lit shader in FindLitShader().");
            }
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("Could not find shader '" + shaderName + "'. Check your project's render-pipeline setup.");
            return shader;
        }

        private static void EnsureFolder(string path)
        {
            string[] sections = path.Split('/');
            string parent = sections[0];
            for (int index = 1; index < sections.Length; index++)
            {
                string next = parent + "/" + sections[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, sections[index]);
                parent = next;
            }
        }

        private static Part[] GetParts()
        {
            // Generated from the model's measured layout, in meters (+Y up, +Z front).
            // All joint twist axes are local +X. Negative elbow rotation bends
            // a downward arm forward; positive knee rotation bends a shin back.
            return new Part[]
            {
                new Part("Pelvis", new Vector3(0f, 0.975f, 0f), new Vector3(0.245f, 0.19f, 0.175f), 1.8f),
                new Part("Chest", new Vector3(0f, 1.285f, 0f), new Vector3(0.29f, 0.335f, 0.19f), 2.6f)
                    .Connect("Pelvis", new Vector3(0f, 1.085f, 0f), -30f, 30f, 25f, 25f),
                new Part("Head", new Vector3(0f, 1.625f, 0f), new Vector3(0.19f, 0.275f, 0.19f), 0.8f, true)
                    .Connect("Chest", new Vector3(0f, 1.48f, 0f), -45f, 45f, 40f, 40f),
                new Part("UpperArm_L", new Vector3(-0.245f, 1.245f, 0f), new Vector3(0.09f, 0.255f, 0.09f), 0.45f, true, new Vector3(0.05f, 0.28f, 0f))
                    .Connect("Chest", new Vector3(-0.22f, 1.385f, 0f), -95f, 95f, 100f, 85f),
                new Part("Forearm_L", new Vector3(-0.2775f, 0.975f, 0f), new Vector3(0.075f, 0.235f, 0.075f), 0.3f, true, new Vector3(0.015f, 0.26f, 0f))
                    .Connect("UpperArm_L", new Vector3(-0.27f, 1.105f, 0f), -140f, 0f, 0f, 0f),
                new Part("Hand_L", new Vector3(-0.285f, 0.795f, 0.008f), new Vector3(0.075f, 0.09f, 0.065f), 0.12f)
                    .Connect("Forearm_L", new Vector3(-0.285f, 0.845f, 0f), -50f, 50f, 25f, 25f),
                new Part("Thigh_L", new Vector3(-0.105f, 0.73f, 0f), new Vector3(0.115f, 0.385f, 0.115f), 0.85f, true, new Vector3(0f, 0.42f, 0f))
                    .Connect("Pelvis", new Vector3(-0.105f, 0.94f, 0f), -100f, 45f, 45f, 35f),
                new Part("Shin_L", new Vector3(-0.105f, 0.325f, 0f), new Vector3(0.085f, 0.355f, 0.085f), 0.55f, true, new Vector3(0f, 0.39f, 0f))
                    .Connect("Thigh_L", new Vector3(-0.105f, 0.52f, 0f), 0f, 140f, 0f, 0f),
                new Part("Foot_L", new Vector3(-0.105f, 0.065f, 0.06f), new Vector3(0.09f, 0.105f, 0.205f), 0.18f)
                    .Connect("Shin_L", new Vector3(-0.105f, 0.13f, 0f), -35f, 35f, 15f, 15f),
                new Part("UpperArm_R", new Vector3(0.245f, 1.245f, 0f), new Vector3(0.09f, 0.255f, 0.09f), 0.45f, true, new Vector3(-0.05f, 0.28f, 0f))
                    .Connect("Chest", new Vector3(0.22f, 1.385f, 0f), -95f, 95f, 100f, 85f),
                new Part("Forearm_R", new Vector3(0.2775f, 0.975f, 0f), new Vector3(0.075f, 0.235f, 0.075f), 0.3f, true, new Vector3(-0.015f, 0.26f, 0f))
                    .Connect("UpperArm_R", new Vector3(0.27f, 1.105f, 0f), -140f, 0f, 0f, 0f),
                new Part("Hand_R", new Vector3(0.285f, 0.795f, 0.008f), new Vector3(0.075f, 0.09f, 0.065f), 0.12f)
                    .Connect("Forearm_R", new Vector3(0.285f, 0.845f, 0f), -50f, 50f, 25f, 25f),
                new Part("Thigh_R", new Vector3(0.105f, 0.73f, 0f), new Vector3(0.115f, 0.385f, 0.115f), 0.85f, true, new Vector3(0f, 0.42f, 0f))
                    .Connect("Pelvis", new Vector3(0.105f, 0.94f, 0f), -100f, 45f, 45f, 35f),
                new Part("Shin_R", new Vector3(0.105f, 0.325f, 0f), new Vector3(0.085f, 0.355f, 0.085f), 0.55f, true, new Vector3(0f, 0.39f, 0f))
                    .Connect("Thigh_R", new Vector3(0.105f, 0.52f, 0f), 0f, 140f, 0f, 0f),
                new Part("Foot_R", new Vector3(0.105f, 0.065f, 0.06f), new Vector3(0.09f, 0.105f, 0.205f), 0.18f)
                    .Connect("Shin_R", new Vector3(0.105f, 0.13f, 0f), -35f, 35f, 15f, 15f),
            };
        }
    }
}
#endif
