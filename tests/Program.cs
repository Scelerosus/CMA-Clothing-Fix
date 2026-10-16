using System.Numerics;
using CMAClothingFix;

OrderChecks.Run();

var target = new CoverageGeometry.Surface(new[] { new Vector3(-.1f,-.1f,0),new Vector3(.1f,-.1f,0),new Vector3(0,.1f,0) }, new[] { new[] {0,1,2}, new[] {0,1,2} });
var normals = Enumerable.Repeat(Vector3.UnitZ,3).ToArray();
CoverageGeometry.Surface Plane(float z, float size = 1) => new(new[] {new Vector3(-size,-size,z),new Vector3(size,-size,z),new Vector3(size,size,z),new Vector3(-size,size,z)},new[]{new[]{0,1,2,0,2,3}});
void Check(bool yes, string name) { if(!yes) throw new Exception(name); Console.WriteLine("PASS " + name); }
CoverageGeometry.Result Run(CoverageGeometry.Surface outer) => CoverageGeometry.Mask(target,normals,outer,new[]{0},CancellationToken.None);
var above = Run(Plane(.02f));
Check(above.Removed==1 && above.Kept[0].Length==0,"opaque outer layer covers target");
Check(above.Kept[1].SequenceEqual(new[]{0,1,2}),"unselected body submesh preserved");
Check(Run(Plane(-.02f)).Removed==1,"skin protruding 2 cm beyond cloth is masked");
Check(Run(Plane(-.04f)).Removed==0,"outside penetration limit preserved");
Check(Run(Plane(.07f)).Removed==0,"distant geometry preserved");
var sidewaysNormals = Enumerable.Repeat(Vector3.UnitX,3).ToArray();
Check(CoverageGeometry.Mask(target,sidewaysNormals,Plane(.002f),new[]{0},CancellationToken.None,0,true).Removed==1,
    "round jewellery is covered despite tangent normals");
Check(CoverageGeometry.Mask(target,sidewaysNormals,Plane(.02f),new[]{0},CancellationToken.None,0,true).Removed==0,
    "jewellery fallback cannot reach distant fabric");
Check(CoverageGeometry.Mask(target,sidewaysNormals,Plane(.02f),new[]{0},CancellationToken.None,.002f).Removed==1,
    "cloth uses face normals when smooth normals miss the outer garment");
Check(Run(Plane(.02f,.08f)).Removed==0,"triangle crossing garment edge preserved");
var refined=CoverageGeometry.Mask(target,normals,Quad(0,-1,1,1),new[]{0},CancellationToken.None,refine:true);
Check(refined.Removed==1 && refined.Added.Length>0 && refined.Kept[0].Length>0,"partially covered triangle is subdivided instead of left intact or fully deleted");
Check(refined.Kept[1].SequenceEqual(target.Triangles[1]),"boundary refinement preserves unselected submeshes");
Check(refined.Added.All(v=>v.Weights.X>=0&&v.Weights.Y>=0&&v.Weights.Z>=0&&Math.Abs(v.Weights.X+v.Weights.Y+v.Weights.Z-1)<1e-6f),"new vertices interpolate original attributes using valid barycentric weights");
Check(refined.Kept.SelectMany(x=>x).All(i=>i>=0&&i<target.Vertices.Length+refined.Added.Length),"refined indices reference existing or generated vertices");
var seamCover=Plane(.02f) with {MinimumTargetY=new[]{.05f}};
Check(Run(seamCover).Removed==0,"waist cover cannot delete triangles below the seam band");
Check(Run(CoverageGeometry.Combine(new[]{seamCover})).Removed==0,"combined covers retain their waist restriction");
Check(Run(CoverageGeometry.Combine(new[]{seamCover,Plane(.025f)})).Removed==1,"ordinary dress coverage remains independent of a restricted shirt seam");
Check(CoverageGeometry.Mask(target,normals,QuadForEdge(),new[]{0},CancellationToken.None,.002f).Removed==1,
    "cloth edge inset removes submillimetre fringe without a body tolerance");
CoverageGeometry.Surface QuadForEdge() => new(new[]{new Vector3(-1,-.0995f,.02f),new Vector3(1,-.0995f,.02f),new Vector3(1,1,.02f),new Vector3(-1,1,.02f)},new[]{new[]{0,1,2,0,2,3}});
Check(Run(QuadForEdge()).Removed==0,"body keeps exact garment boundary");
Check(CoverageGeometry.Mask(target,normals,Quad(-1,-.102f,1,1),new[]{0},CancellationToken.None,skin:true).Removed==0,
    "skin within 4 mm of a neckline remains visible");
Check(CoverageGeometry.Mask(target,normals,Plane(.02f),new[]{0},CancellationToken.None,skin:true).Removed==1,
    "skin well inside opaque fabric still receives clipping protection");
Check(CoverageGeometry.Mask(target,normals,Quad(-1,-.102f,1,1),new[]{0},CancellationToken.None,.002f).Removed==1,
    "skin edge protection does not change garment masks");
Check(CoverageGeometry.Mask(target,normals,Plane(.02f),new[]{0},CancellationToken.None,skin:true,keepVertex:new[]{false,true,false}).Removed==0,
    "triangles touching protected extremity vertices are never hidden");
Check(CoverageGeometry.Mask(target,normals,Plane(.02f),new[]{0},CancellationToken.None,skin:true,keepBelowY:10f).Removed==0
    && CoverageGeometry.Mask(target,normals,Plane(.02f),new[]{0},CancellationToken.None,skin:true,keepBelowY:10f,toeVertex:new[]{false,true,false}).Removed==1,
    "toe triangles stay maskable below the foot exemption");
Check(CoverageGeometry.Mask(target,normals,Quad(-1,-.102f,1,1),new[]{0},CancellationToken.None,.002f).Removed==1
    && CoverageGeometry.Mask(target,normals,Quad(-1,-.102f,1,1),new[]{0},CancellationToken.None,.002f,hemKeep:.03f).Removed==0,
    "cloth just under an outer hem is kept when hemKeep is set");
CoverageGeometry.SkinInfo Skin(float scale = 1) => new(new[]{new[]{scale,0,0,0,scale,0,0,0,scale}}, new int[12], Enumerable.Range(0,12).Select(i=>i%4==0?1f:0f).ToArray());
CoverageGeometry.Result Push(CoverageGeometry.Surface outer, float scale = 1) => CoverageGeometry.Push(target,normals,outer,new[]{0},Skin(scale),.03f,.002f,CancellationToken.None);
var pushed = Push(Plane(-.01f));
Check(pushed.Removed==3 && Math.Abs(pushed.Offsets[0].Z+.012f)<1e-5f && pushed.Offsets[0].X==0,"vertex poking 1 cm through cloth is pushed 1.2 cm back under it");
Check(Push(Plane(.02f)).Removed==0,"vertex well beneath cloth is not moved");
Check(Math.Abs(Push(Plane(.001f)).Offsets[0].Z+.001f)<1e-5f,"vertex too close under cloth is pushed to the 2 mm margin");
Check(Math.Abs(Push(Plane(-.01f),2).Offsets[0].Z+.006f)<1e-5f,"world push is converted through the skin matrix to rest space");
Check(Push(Plane(-.05f)).Removed==0,"cloth farther than the push limit is ignored");
var snapResult = CoverageGeometry.Mask(target,normals,Quad(-1,0,1,1),new[]{0},CancellationToken.None,snapSkin:Skin());
Check(snapResult.Removed==0 && snapResult.Offsets!=null && Math.Abs(snapResult.Offsets[2].Y+.1f)<.01f && snapResult.Offsets[0]==Vector3.Zero && snapResult.Offsets[1]==Vector3.Zero,
    "covered boundary vertex snaps to the garment edge, uncovered vertices stay");
Check(CoverageGeometry.Mask(target,normals,Quad(-1,0,1,1),new[]{0},CancellationToken.None).Offsets==null,"no snapping unless requested");
CoverageGeometry.Result Inflate(CoverageGeometry.Surface body, Vector3[] n = null) => CoverageGeometry.Inflate(target,n ?? normals,body,new[]{0},Skin(),.03f,.002f,CancellationToken.None);
var inflated = Inflate(Plane(.01f));
Check(inflated.Removed==3 && Math.Abs(inflated.Offsets[0].Z-.012f)<1e-5f && inflated.Offsets[0].X==0,"garment sunk 1 cm into the skin is pushed out to 2 mm above it");
Check(Inflate(Plane(-.02f)).Removed==0,"garment well above the skin is not moved");
Check(Math.Abs(Inflate(Plane(-.001f)).Offsets[0].Z-.001f)<1e-5f,"garment just above the skin is lifted to the margin");
Check(Inflate(Plane(.01f),Enumerable.Repeat(-Vector3.UnitZ,3).ToArray()).Removed==0,"inverted garment normals are left alone");
Check(Inflate(Plane(.05f)).Removed==0,"skin farther than the push limit is ignored");
var ramp = new CoverageGeometry.Surface(new[]{new Vector3(-.1f,-.1f,0),new Vector3(.1f,-.1f,.03f),new Vector3(0,.1f,.03f)},new[]{new[]{0,1,2}});
var smoothed = CoverageGeometry.Inflate(ramp,normals,Plane(.005f),new[]{0},Skin(),.03f,.002f,CancellationToken.None);
Check(Math.Abs(smoothed.Offsets[0].Z-.007f)<1e-5f && Math.Abs(smoothed.Offsets[1].Z-.0049f)<1e-5f && Math.Abs(smoothed.Offsets[2].Z-.0049f)<1e-5f,
    "lift spreads to neighbours with falloff and never reduces the needed lift");
Check(CoverageGeometry.Inflate(ramp,normals,Plane(.005f),new[]{0},Skin(),.03f,.002f,CancellationToken.None,0).Offsets[1]==Vector3.Zero,"smoothing can be switched off");
var tightPull = CoverageGeometry.Inflate(target,normals,Plane(-.01f),new[]{0},Skin(),.03f,.001f,CancellationToken.None,0,.02f);
Check(Math.Abs(tightPull.Offsets[0].Z+.009f)<1e-5f,"tight garment hovering 1 cm above the skin is pulled in to the margin");
Check(CoverageGeometry.Inflate(target,normals,Plane(-.03f),new[]{0},Skin(),.03f,.001f,CancellationToken.None,0,.02f).Removed==0,"tight pull ignores garments hovering beyond its reach");
var absent = new CoverageGeometry.Surface(Array.Empty<Vector3>(),new[]{Array.Empty<int>()});
Check(Run(absent).Removed==0,"empty cover does not delete geometry");
using var cancel = new CancellationTokenSource(); cancel.Cancel();
bool cancelled=false;
try { CoverageGeometry.Mask(target,normals,Plane(.02f),new[]{0},cancel.Token); } catch(OperationCanceledException) {cancelled=true;}
Check(cancelled,"outfit change cancels geometry computation");

CoverageGeometry.Surface Grid(bool hole)
{
    var vertices = new List<Vector3>(); var indices = new List<int>();
    for(int x=0;x<10;x++) for(int y=0;y<10;y++)
    {
        if(hole && x>=4 && x<=5 && y>=4 && y<=5) continue;
        float a=-1+x*.2f, b=-1+y*.2f; int start=vertices.Count;
        vertices.AddRange(new[]{new Vector3(a,b,.02f),new Vector3(a+.2f,b,.02f),new Vector3(a+.2f,b+.2f,.02f),new Vector3(a,b+.2f,.02f)});
        indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
    }
    return new(vertices.ToArray(),new[]{indices.ToArray()});
}
Check(Run(Grid(false)).Removed==1,"BVH traversal through subdivided outer mesh");
Check(Run(Grid(true)).Removed==0,"opening in dense outer mesh preserves skin");

var combined = CoverageGeometry.Combine(new[] { Plane(.07f), Plane(.02f) });
Check(combined.Triangles[1].SequenceEqual(new[]{4,5,6,4,6,7}),"combined cover indices retain vertex offsets");
Check(Run(combined).Removed==1,"multiple outer garments contribute to coverage");
Check(Run(CoverageGeometry.Combine(Array.Empty<CoverageGeometry.Surface>())).Removed==0,"no outer garments preserves target");

CoverageGeometry.Surface Quad(float x0, float y0, float x1, float y1, float z = .02f) => new(
    new[] { new Vector3(x0,y0,z), new Vector3(x1,y0,z), new Vector3(x1,y1,z), new Vector3(x0,y1,z) },
    new[] { new[] { 0,1,2,0,2,3 } });
var strap = new CoverageGeometry.Surface(new[] { new Vector3(-.001f,-.1f,0),new Vector3(.001f,-.1f,0),new Vector3(0,.1f,0) }, new[] { new[] { 0,1,2 } });
Check(CoverageGeometry.Mask(strap,normals,Quad(-.0012f,-.11f,.0012f,.11f),new[]{0},CancellationToken.None).Removed==1,
    "thin covered triangles do not require samples outside their surface");
var edgeOpening = CoverageGeometry.Combine(new[] {
    Quad(-1,-1,-.005f,1), Quad(.005f,-1,1,1), Quad(-.005f,-1,.005f,-.105f), Quad(-.005f,-.095f,.005f,1)
});
Check(Run(edgeOpening).Removed==0,"opening between covered corners is preserved by edge sampling");
Check(CoverageGeometry.Mask(target,normals,edgeOpening,new[]{0},CancellationToken.None,refine:true).Kept[0].Length>0,
    "boundary refinement preserves visible geometry through a garment opening");
Check(CoverageGeometry.Mask(target,Array.Empty<Vector3>(),Plane(.02f),new[]{0},CancellationToken.None).Removed==1,
    "missing posed normals use geometric face normals");
Check(CoverageGeometry.Mask(target,new[]{Vector3.Zero,new Vector3(float.NaN),Vector3.Zero},Plane(.02f),new[]{0},CancellationToken.None).Removed==1,
    "invalid posed normals use geometric face normals");

var curvedNormals = new[] { Vector3.Normalize(new Vector3(-.6f,-.3f,1)),Vector3.Normalize(new Vector3(.6f,-.3f,1)),Vector3.Normalize(new Vector3(0,.6f,1)) };
CoverageGeometry.Surface Patch(Vector3 p, Vector3 n)
{
    n=Vector3.Normalize(n); p+=n*.02f;
    var x=Vector3.Normalize(Vector3.Cross(n,Vector3.UnitY))*.003f; var y=Vector3.Cross(n,x);
    return new(new[]{p-x-y,p+x-y,p+x+y,p-x+y},new[]{new[]{0,1,2,0,2,3}});
}
var samples = new[]{new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,1),
    new Vector3(.5f,.5f,0),new Vector3(0,.5f,.5f),new Vector3(.5f,0,.5f),new Vector3(1f/3,1f/3,1f/3),
    new Vector3(.5f,.25f,.25f),new Vector3(.25f,.5f,.25f),new Vector3(.25f,.25f,.5f)};
var curvedCover = CoverageGeometry.Combine(samples.Select(w => Patch(
    target.Vertices[0]*w.X+target.Vertices[1]*w.Y+target.Vertices[2]*w.Z,
    curvedNormals[0]*w.X+curvedNormals[1]*w.Y+curvedNormals[2]*w.Z)));
Check(CoverageGeometry.Mask(target,curvedNormals,curvedCover,new[]{0},CancellationToken.None).Removed==1,
    "curved posed surfaces project each sample along its local normal");
bool combineCancelled=false;
try { CoverageGeometry.Combine(new[]{Plane(.02f)},cancel.Token); } catch(OperationCanceledException) { combineCancelled=true; }
Check(combineCancelled,"outfit changes cancel cover concatenation");
bool emptyCancelled=false;
try { CoverageGeometry.Mask(target,normals,absent,new[]{0},cancel.Token); } catch(OperationCanceledException) { emptyCancelled=true; }
Check(emptyCancelled,"cancellation is honored even when cover geometry is empty");

CoverageGeometry.AlphaImage Image(byte value) => new(16,16,Enumerable.Repeat(value,256).ToArray());
CoverageGeometry.Opacity Opacity(CoverageGeometry.AlphaImage image, float minimum = .995f, int wrap = 1) =>
    new(image,Vector2.One,Vector2.Zero,minimum,wrap,wrap);
CoverageGeometry.Surface Textured(CoverageGeometry.Opacity opacity, float z = .02f)
{
    var plane = Plane(z);
    return plane with { UV = new[] { Vector2.Zero,Vector2.UnitX,Vector2.One,Vector2.UnitY }, Opacities = new[] { opacity } };
}
Check(Run(Textured(Opacity(Image(255)))).Removed==1,"fully opaque texture alpha covers skin");
Check(Run(Textured(Opacity(Image(128)))).Removed==0,"transparent fabric never hides skin under its see-through texels");
Check(Run(Textured(Opacity(Image(200),.55f))).Removed==1,"cutout threshold accepts fully visible texels");
Check(Run(Textured(Opacity(Image(200),.9f))).Removed==0,"cutout threshold preserves clipped texels");
var holeAlpha = Image(255);
for(int x=7;x<=8;x++) for(int y=7;y<=8;y++) holeAlpha.Pixels[y*16+x]=0;
Check(Run(Textured(Opacity(holeAlpha))).Removed==0,"alpha opening preserves the skin visible through it");
var invalidUV = Textured(Opacity(Image(255))) with { UV = null };
Check(Run(invalidUV).Removed==0,"missing texture UV fails closed");
Check(Run(CoverageGeometry.Combine(new[]{invalidUV})).Removed==0,"combining surfaces never replaces missing UV with opaque coordinates");
Check(Run(CoverageGeometry.Combine(new[]{Textured(Opacity(Image(0)),.025f),Textured(Opacity(Image(255)))})).Removed==1,
    "transparent first hit continues to a separate opaque outer layer");
var mixedAlpha = CoverageGeometry.Combine(new[]{Textured(Opacity(Image(255)),.07f),Textured(Opacity(Image(0)))});
Check(Run(mixedAlpha).Removed==0,"combined garments preserve each submesh alpha instead of sharing the first material");
Check(Run(Textured(Opacity(Image(255),1.01f))).Removed==0,"material alpha below the opacity threshold cannot hide skin");
var stripe = new CoverageGeometry.AlphaImage(8,8,Enumerable.Range(0,64).Select(i=>(byte)(i%8<4?255:0)).ToArray());
Check(Opacity(stripe,wrap:0).Covers(new Vector2(1.125f,.5f)),"repeat alpha addressing follows UV tiles");
Check(!Opacity(stripe,wrap:0).Covers(new Vector2(-.125f,.5f)),"negative repeated UV reaches the transparent tile edge");
Check(Opacity(stripe,wrap:1).Covers(new Vector2(-.125f,.5f)),"clamped alpha addressing stays on the texture edge");
Check(!Opacity(stripe,wrap:2).Covers(new Vector2(1.125f,.5f)),"mirrored alpha addressing reflects alternate tiles");
Check(Opacity(stripe,wrap:2).Covers(new Vector2(-.125f,.5f)),"negative mirrored UV reflects correctly");
Check(Opacity(stripe,wrap:3).Covers(new Vector2(-.125f,.5f)),"mirror-once alpha addressing reflects before clamping");
Check(!Opacity(stripe,wrap:3).Covers(new Vector2(2.125f,.5f)),"mirror-once UV clamps beyond the reflected range");
Check((Opacity(stripe) with {Scale=new Vector2(.5f,1)}).Covers(new Vector2(.25f,.5f)),"material UV scale is applied before alpha sampling");
Check(!(Opacity(stripe) with {Offset=new Vector2(.5f,0)}).Covers(new Vector2(.125f,.5f)),"material UV offset is applied before alpha sampling");
Check(!Opacity(stripe).Covers(new Vector2(float.NaN,.5f)),"invalid texture coordinates preserve original geometry");
Check(!Opacity(new CoverageGeometry.AlphaImage(8,8,new byte[1])).Covers(Vector2.Zero),"invalid alpha dimensions fail closed");
Check(!Opacity(new CoverageGeometry.AlphaImage(8,8,null)).Covers(Vector2.Zero),"missing alpha pixels fail closed");
Check(!Opacity(stripe with { Width = 0 }).Covers(Vector2.Zero),"empty alpha images fail closed");
Check(!Opacity(stripe,wrap:99).Covers(Vector2.Zero),"unknown texture wrap modes fail closed");
Check(Opacity(stripe).Signature != Opacity(stripe with { Revision = 1 }).Signature,"texture changes invalidate pending masks");
