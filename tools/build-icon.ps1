# Regenerate the PNG preview and Windows ICO from the original vector geometry.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing,System.Xml,System.Xml.Linq -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Xml.Linq;
public static class ClearDeskIconBuilder {
 static float Value(XElement e, string key) { return float.Parse(e.Attribute(key).Value, System.Globalization.CultureInfo.InvariantCulture); }
 static GraphicsPath Round(float x,float y,float w,float h,float r) {
  var p=new GraphicsPath(); float d=r*2;
  p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
  p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90); p.CloseFigure(); return p;
 }
 public static void Build(string svg,string png,string ico) {
  using(var master=new Bitmap(1024,1024,PixelFormat.Format32bppArgb)) {
   using(var g=Graphics.FromImage(master)) {
    g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(2,2);
    foreach(var e in XDocument.Load(svg).Root.Elements()) {
     if(e.Name.LocalName!="rect") continue;
     using(var path=Round(Value(e,"x"),Value(e,"y"),Value(e,"width"),Value(e,"height"),Value(e,"rx")))
     using(var brush=new SolidBrush(ColorTranslator.FromHtml(e.Attribute("fill").Value))) g.FillPath(brush,path);
    }
   }
   using(var preview=Resize(master,512)) preview.Save(png,ImageFormat.Png);
   using(var small=Resize(master,128)) small.Save(Path.Combine(Path.GetDirectoryName(png),"ClearDesk-128.png"),ImageFormat.Png);
   int[] sizes={16,20,24,32,40,48,64,128,256}; var images=new byte[sizes.Length][];
   for(int i=0;i<sizes.Length;i++) using(var b=Resize(master,sizes[i])) {
    int s=sizes[i], stride=((s+31)/32)*4;
    using(var buffer=new MemoryStream()) using(var w=new BinaryWriter(buffer)) {
     w.Write(40); w.Write(s); w.Write(s*2); w.Write((short)1); w.Write((short)32);
     w.Write(0); w.Write(s*s*4+stride*s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
     for(int y=s-1;y>=0;y--) for(int x=0;x<s;x++) { Color c=b.GetPixel(x,y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
     // AND mask for readers which still use the classic icon transparency mask.
     for(int y=s-1;y>=0;y--) {
      byte[] row=new byte[stride]; for(int x=0;x<s;x++) if(b.GetPixel(x,y).A==0) row[x/8]|=(byte)(0x80>>(x%8)); w.Write(row);
     }
     images[i]=buffer.ToArray();
    }
   }
   using(var w=new BinaryWriter(File.Create(ico))) {
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); int offset=6+16*sizes.Length;
    for(int i=0;i<sizes.Length;i++) { w.Write((byte)(sizes[i]==256?0:sizes[i])); w.Write((byte)(sizes[i]==256?0:sizes[i])); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset); offset+=images[i].Length; }
    foreach(byte[] data in images) w.Write(data);
   }
  }
 }
 static Bitmap Resize(Bitmap source,int size) {
  var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
  using(var g=Graphics.FromImage(b)) { g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.DrawImage(source,0,0,size,size); } return b;
 }
}
'@
$projectRoot = Split-Path $PSScriptRoot -Parent
[ClearDeskIconBuilder]::Build((Join-Path $projectRoot 'assets\ClearDesk.svg'), (Join-Path $projectRoot 'assets\ClearDesk.png'), (Join-Path $projectRoot 'assets\ClearDesk.ico'))
Write-Host 'Generated assets/ClearDesk.png and assets/ClearDesk.ico'
