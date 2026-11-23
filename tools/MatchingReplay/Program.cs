using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Vision;

// Read-only replay: no camera, PLC, UI or recipe writes. Optional overrides live in memory only.
if(args.Length<2)throw new ArgumentException("Usage: MatchingReplay recipe.json result-directory [Akaze|Sift|Orb|OrbMaxStable|Normal] [Parameter=value ...]");
var json=new JsonSerializerOptions {PropertyNameCaseInsensitive=true,WriteIndented=true};
json.Converters.Add(new JsonStringEnumConverter());
var recipe=JsonSerializer.Deserialize<Recipe>(File.ReadAllText(args[0]),json)!;
for(int end=0;end<recipe.Ends.Length;end++)
{
    var template=recipe.Ends[end].Terminal!.Copy();
    template=template with {TemplatePng=File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,template.TemplateImage))};
    if(args.Length>2)template=template with {Algorithm=Enum.Parse<MatchingAlgorithm>(args[2],true)};
    var parameters=new Dictionary<MatchParameter,double>(template.ActiveParameters());
    foreach(var option in args.Skip(3)){var kv=option.Split('=');parameters[Enum.Parse<MatchParameter>(kv[0],true)]=double.Parse(kv[1],CultureInfo.InvariantCulture);}
    template=template with {Profiles=new(){[template.Algorithm]=parameters}};
    var bytes=File.ReadAllBytes(Path.Combine(args[1],$"end{end+1}.ppm"));int position=0;
    string Token(){while(position<bytes.Length&&char.IsWhiteSpace((char)bytes[position]))position++;int start=position;while(position<bytes.Length&&!char.IsWhiteSpace((char)bytes[position]))position++;return System.Text.Encoding.ASCII.GetString(bytes,start,position-start);}
    if(Token()!="P6")throw new InvalidDataException("Expected app-generated P6 image.");
    int width=int.Parse(Token()),height=int.Parse(Token());if(Token()!="255")throw new InvalidDataException("Expected 8-bit PPM.");
    if(bytes[position++]=='\r'&&bytes[position]=='\n')position++;
    var bgr=bytes[position..];if(bgr.Length!=width*height*3)throw new InvalidDataException("Invalid PPM size.");
    for(int i=0;i<bgr.Length;i+=3)(bgr[i],bgr[i+2])=(bgr[i+2],bgr[i]);
    var frame=new ImageFrame(width,height,width*3,bgr,Guid.NewGuid(),DateTimeOffset.UtcNow,"OFFLINE REPLAY");
    var result=await new NativeTemplateMatcher().MatchAsync(frame,template,default);
    Console.WriteLine(JsonSerializer.Serialize(new{End=end+1,recipe.ModelCode,recipe.Revision,Result=result with {AlignedPng=[],TemplatePng=[]}},json));
}
