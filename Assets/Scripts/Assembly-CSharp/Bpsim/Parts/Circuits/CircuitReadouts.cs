using System.Collections.Generic;
using System.Globalization;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.Parts.Circuits
{
    /// <summary>Readouts allocate text only when its rounded value changes (at most 10 Hz).</summary>
    internal static class CircuitReadouts
    {
        private sealed class Readout { public TextMesh Mesh; public float Next; public double Value=double.NaN; public int Digit=-1; public bool Failed; public byte Mode; }
        private static readonly Dictionary<Entity,Readout> items=new Dictionary<Entity,Readout>();
        private static Font font;
        public static void Update(Entity entity,CircuitPart data,float3 position)
        {
            if(!data.IsIndicator&&data.Kind!=CircuitKind.DigitalDisplay) return;
            if(!items.TryGetValue(entity,out Readout r))
            {
                if(font==null) font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var go=new GameObject("CircuitReadout");r=new Readout { Mesh=go.AddComponent<TextMesh>() };items.Add(entity,r);
                r.Mesh.font=font;r.Mesh.fontSize=48;r.Mesh.characterSize=data.IsIndicator?.012f:.022f;
                r.Mesh.anchor=TextAnchor.MiddleCenter;r.Mesh.alignment=TextAlignment.Center;
                r.Mesh.color=data.IsIndicator?Color.black:new Color(.1f,1,.25f);
                var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=font.material;renderer.sortingOrder=30;
            }
            r.Mesh.transform.position=new Vector3(position.x,position.y,position.z-.07f);
            if(Time.unscaledTime<r.Next)return;r.Next=Time.unscaledTime+.1f;
            if(data.Kind==CircuitKind.DigitalDisplay)
            {
                int digit=data.DisplayValue&15;if(r.Digit==digit)return;r.Digit=digit;r.Mesh.text=digit.ToString("X");
            }
            else
            {
                // Keep four significant digits so microampere/leakage readings do not
                // disappear into 0.00. Quantize numerically before formatting text.
                double value=data.Kind==CircuitKind.Voltmeter?data.Voltage:data.Current;
                bool ohmmeter=data.Kind==CircuitKind.Ammeter&&data.Mode!=0;
                if(ohmmeter)value=System.Math.Abs(value)<1e-15?double.PositiveInfinity:System.Math.Max(0,5/System.Math.Abs(value)-100);
                if(value!=0&&!double.IsNaN(value)&&!double.IsInfinity(value))
                {double scale=System.Math.Pow(10,3-System.Math.Floor(System.Math.Log10(System.Math.Abs(value))));value=System.Math.Round(value*scale)/scale;}
                if(r.Value==value&&r.Failed==data.SolveFailed&&r.Mode==data.Mode)return;r.Value=value;r.Failed=data.SolveFailed;r.Mode=data.Mode;
                r.Mesh.text=data.SolveFailed||double.IsNaN(value)?"ERR":double.IsInfinity(value)?(ohmmeter?"OPEN":"ERR"):value.ToString("G4",CultureInfo.InvariantCulture)+(ohmmeter?"Ω":data.Kind==CircuitKind.Voltmeter?"V":"A");
            }
        }
        public static void Remove(Entity entity)
        {
            if(!items.TryGetValue(entity,out var r))return;
            if(r.Mesh!=null) Object.Destroy(r.Mesh.gameObject);items.Remove(entity);
        }
        public static void Clear()
        {
            foreach(var r in items.Values)if(r.Mesh!=null)Object.Destroy(r.Mesh.gameObject);
            items.Clear();CircuitControls.Close();
        }
    }

    /// <summary>A runtime slider for the adjustable resistor and the display's four-bit output.</summary>
    internal sealed class CircuitControls : MonoBehaviour
    {
        private static CircuitControls instance;
        private Entity entity;
        private Slider slider;
        private Text label;
        private bool display;
        private static Text Text(Transform parent,string value,int size)
        {
            var go=new GameObject("Label",typeof(RectTransform));go.transform.SetParent(parent,false);
            var text=go.AddComponent<Text>();text.font=Mods.ModUiKit.UiFont;text.fontSize=size;text.text=value;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;return text;
        }
        private static RectTransform Rect(Transform parent,string name,Vector2 size,Vector2 position,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;rect.sizeDelta=size;rect.anchoredPosition=position;go.GetComponent<Image>().color=color;return rect;
        }
        public static void Show(Entity part,bool output)
        {
            Close();var go=new GameObject("CircuitControls");instance=go.AddComponent<CircuitControls>();instance.entity=part;instance.display=output;
            var canvas=go.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=500;
            var scaler=go.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);go.AddComponent<GraphicRaycaster>();
            var panel=Rect(go.transform,"Panel",new Vector2(360,150),new Vector2(0,-220),new Color(.08f,.09f,.12f,.97f));
            instance.label=Text(panel,"",22);((RectTransform)instance.label.transform).sizeDelta=new Vector2(330,40);((RectTransform)instance.label.transform).anchoredPosition=new Vector2(0,42);
            var track=Rect(panel,"Slider",new Vector2(280,26),new Vector2(0,0),new Color(.3f,.3f,.35f));
            var handle=Rect(track,"Handle",new Vector2(22,32),Vector2.zero,new Color(.3f,.8f,1));
            var s=track.gameObject.AddComponent<Slider>();instance.slider=s;s.direction=Slider.Direction.LeftToRight;s.handleRect=handle;s.targetGraphic=handle.GetComponent<Image>();
            s.minValue=output?0:.1f;s.maxValue=output?15:10;s.wholeNumbers=output;
            var em=Gameplay.PartGameplayUtil.Em;var data=em.GetComponentData<CircuitPart>(part);s.value=output?data.DisplayValue:data.ValueA;
            instance.SetValue(s.value);s.onValueChanged.AddListener(instance.SetValue);
            var close=Rect(panel,"Close",new Vector2(110,32),new Vector2(0,-48),new Color(.25f,.3f,.4f));
            var text=Text(close,"关闭",18);var rect=(RectTransform)text.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.sizeDelta=Vector2.zero;
            close.gameObject.AddComponent<Button>().onClick.AddListener(Close);
        }
        private void SetValue(float value)
        {
            var em=Gameplay.PartGameplayUtil.Em;if(!em.Exists(entity)||!em.HasComponent<CircuitPart>(entity))return;
            var data=em.GetComponentData<CircuitPart>(entity);
            if(display){data.DisplayValue=Mathf.RoundToInt(value);label.text="数字输出 0x"+data.DisplayValue.ToString("X");}
            else{data.ValueA=value;label.text="电阻 "+value.ToString("0.00")+" Ω";}
            em.SetComponentData(entity,data);
        }
        private void Update()
        {
            var manager=PartManager.Instance;
            if(manager==null||!manager.IsSimulating||!Gameplay.PartGameplayUtil.Alive(entity))Close();
        }
        public static void Close() { if(instance!=null)Object.Destroy(instance.gameObject);instance=null; }
    }
}
