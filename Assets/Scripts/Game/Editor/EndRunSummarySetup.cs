using System;
using System.Linq;
using Oculus.Interaction;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TakeMeToTheMoon.Story;
using Object = UnityEngine.Object;

namespace TakeMeToTheMoon.Game.Editor
{
    public static class EndRunSummarySetup
    {
        const string Path = "Assets/Prefabs/Game Flow/End Screen.prefab";
        static TMP_FontAsset heading, body;
        static readonly Color Cream = new Color(.91f,.89f,.79f);
        static readonly Color Amber = new Color(.91f,.52f,.16f);
        [MenuItem("Tools/Game Flow/Build End Summary Screen")]
        public static void Build()
        {
            heading = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/RubikDirt-Regular SDF.asset");
            body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/EastSeaDokdo-Regular SDF.asset");
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                foreach (Transform child in root.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                var flow = new SerializedObject(root.GetComponent<GameFlowScreen>());
                flow.FindProperty("returnToStartButton").objectReferenceValue = null;
                flow.ApplyModifiedPropertiesWithoutUndo();
                var canvasGo = new GameObject("End Summary Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(root.transform, false);
                var rect = (RectTransform)canvasGo.transform;
                rect.sizeDelta = new Vector2(1280, 720);
                rect.localPosition = new Vector3(0, 1.5f, 2);
                rect.localScale = Vector3.one * .0036f;
                var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                Box(rect,"Black Background",0,0,1280,720,Color.black);
                Box(rect,"Traveler Panel",0,210,900,232,new Color(.035f,.04f,.025f,1),new Color(.55f,.34f,.12f,.9f));
                var portrait = Box(rect,"Traveler Portrait",0,210,900,232,Color.white).GetComponent<Image>(); portrait.preserveAspect=true;
                var status = Text(rect,"Status","YOU ARE",0,280,800,20,16,heading);
                var title = Text(rect,"Traveler Title","The Guardian",0,230,800,64,48,heading);
                var blurb = Text(rect,"Traveler Blurb",TravelerArchetypes.Blurbs[0],0,160,800,60,30,body);
                var detail = Text(rect,"Traveler Detail","",0,82,1120,22,20,body);
                Box(rect,"Wellness Card",-285,38,550,62,new Color(.06f,.08f,.03f,1),new Color(.56f,.7f,.24f));
                Box(rect,"Throws Card",285,38,550,62,new Color(.07f,.055f,.025f,1),new Color(.5f,.32f,.13f));
                Text(rect,"Wellness Caption","FINAL WELLNESS",-280,54,510,16,12,heading);
                Text(rect,"Throws Caption","THROWS USED",290,54,510,16,12,heading);
                var wellness = Text(rect,"Wellness","85",-280,28,510,32,28,heading);
                var throws = Text(rect,"Throws","3 / 3",290,28,510,32,28,heading); throws.color=Amber;
                Text(rect,"Choices Heading","YOUR CHOICES",0,-12,1120,20,12,heading);
                var rows = new TMP_Text[5];
                for(int i=0;i<5;i++)
                {
                    float y=-50-i*46;
                    Box(rect,"Choice "+(i+1),0,y,1120,40,new Color(.05f,.06f,.04f,1),new Color(.26f,.27f,.20f,.6f));
                    var number=Text(rect,"Number "+(i+1),"{"+(i+1).ToString("00")+"}",-510,y,64,32,18,heading);number.color=Amber;
                    rows[i]=Text(rect,"Choice Text "+(i+1),"—",38,y,1000,36,29,body);
                }
                var buttonPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("ce5610a59dc7847a68bbf28c03fec7a1"));
                var buttonGo=(GameObject)PrefabUtility.InstantiatePrefab(buttonPrefab,rect);
                buttonGo.name="Play Again (Primary Button)";
                var br=(RectTransform)buttonGo.transform; br.anchorMin=br.anchorMax=br.pivot=new Vector2(.5f,.5f);
                br.anchoredPosition=new Vector2(0,-310); br.localScale=Vector3.one*.8f;
                buttonGo.GetComponentInChildren<TMP_Text>(true).text="PLAY AGAIN";
                var summary=root.GetComponent<EndRunSummary>() ?? root.AddComponent<EndRunSummary>();
                var serialized=new SerializedObject(summary);
                void Assign(string field,Object value)=>serialized.FindProperty(field).objectReferenceValue=value;
                Assign("portrait",portrait);Assign("status",status);Assign("title",title);Assign("blurb",blurb);Assign("detail",detail);
                Assign("wellness",wellness);Assign("throws",throws);Assign("playAgain",buttonGo.GetComponent<Button>());
                var choices=serialized.FindProperty("choices");choices.arraySize=5;
                for(int i=0;i<5;i++)choices.GetArrayElementAtIndex(i).objectReferenceValue=rows[i];
                var portraits=serialized.FindProperty("portraits");portraits.arraySize=5;
                string[] files={"Guardian","Redeemed","LoyalOne","Prepared","Survivor"};
                for(int i=0;i<5;i++)
                {
                    string path="Assets/Images/"+files[i]+".png";
                    var sprite=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                    if(sprite==null)throw new InvalidOperationException("Sprite missing: "+path);
                    var entry=portraits.GetArrayElementAtIndex(i);entry.FindPropertyRelative("travelerType").stringValue=TravelerArchetypes.Names[i];
                    entry.FindPropertyRelative("image").objectReferenceValue=sprite;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                summary.Show("The Guardian",85,3,new[] {
                    new StoryHistoryEntry{checkpoint=1,picked="Grab your roommate and leave!"},
                    new StoryHistoryEntry{checkpoint=2,picked="Hitch a pickup to the launch pad"},
                    new StoryHistoryEntry{checkpoint=3,picked="Fill the fuel: 3/3 targets"},
                    new StoryHistoryEntry{checkpoint=4,picked="Help the stranded crew board your capsule"},
                    new StoryHistoryEntry{checkpoint=5,picked="Give Kyle your seat on the Moon shuttle"}
                });
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("8369d93f7b6b99742bbea0649a41b7b1"));
                var interaction=Object.Instantiate(source,rect,false);interaction.name="ISDK_RayCanvasInteraction";
                var ir=(RectTransform)interaction.transform;ir.anchorMin=Vector2.zero;ir.anchorMax=Vector2.one;ir.offsetMin=ir.offsetMax=Vector2.zero;
                interaction.GetComponent<PointableCanvas>().InjectCanvas(canvas);
                PrefabUtility.SaveAsPrefabAsset(root,Path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            Preview();
            Debug.Log("[EndSummary] Built end screen with five portraits, stats, history, Meta ray input and reset. Preview: /tmp/end-summary-preview.png");
        }
        static RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color,Color? border=null)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(w,h);rect.anchoredPosition=new Vector2(x,y);
            var image=go.GetComponent<Image>();image.color=new Color(color.r,color.g,color.b,border.HasValue ? 1 : color.a);image.raycastTarget=false;
            if(border.HasValue){var outline=go.AddComponent<Outline>();outline.effectColor=border.Value;outline.effectDistance=new Vector2(1,-1);}
            return rect;
        }
        static TMP_Text Text(Transform parent,string name,string copy,float x,float y,float w,float h,float size,TMP_FontAsset font)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;rect.sizeDelta=new Vector2(w,h);rect.anchoredPosition=new Vector2(x,y);
            var text=go.GetComponent<TextMeshProUGUI>();text.font=font;text.text=copy;text.fontSize=size;text.enableAutoSizing=true;
            text.fontSizeMax=size;text.fontSizeMin=size*.7f;text.alignment=TextAlignmentOptions.MidlineLeft;text.color=Cream;text.raycastTarget=false;text.richText=false;
            return text;
        }
        [MenuItem("Tools/Game Flow/Preview End Summary Screen")]
        public static void Preview()
        {
            var scene=EditorSceneManager.NewPreviewScene();RenderTexture target=null;Texture2D result=null;var old=RenderTexture.active;
            try{
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
                var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                var canvas=root.GetComponentInChildren<Canvas>();canvas.transform.localPosition=new Vector3(0,0,2);canvas.transform.localScale=Vector3.one*.001f;
                var cameraGo=new GameObject("Preview Camera",typeof(Camera));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo,scene);
                var camera=cameraGo.GetComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=.36f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.nearClipPlane=.01f;camera.farClipPlane=10;
                foreach(var text in root.GetComponentsInChildren<TextMeshProUGUI>())text.ForceMeshUpdate();Canvas.ForceUpdateCanvases();
                target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();camera.targetTexture=null;
                RenderTexture.active=target;result=new Texture2D(1280,720,TextureFormat.RGB24,false);result.ReadPixels(new Rect(0,0,1280,720),0,0);result.Apply();
                System.IO.File.WriteAllBytes("/tmp/end-summary-preview.png",result.EncodeToPNG());
            }finally{RenderTexture.active=old;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(result!=null)Object.DestroyImmediate(result);EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
}
