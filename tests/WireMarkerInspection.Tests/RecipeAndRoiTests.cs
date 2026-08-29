using System.IO;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;
namespace WireMarkerInspection.Tests;
public class RecipeAndRoiTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-tests-"+Guid.NewGuid().ToString("N"));
    [Fact]public void PolygonAndCircleValidation()
    {
        Assert.NotNull(new SearchRoi(RoiShape.Polygon,[new(0,0),new(90,90),new(0,90),new(90,0)]).Validate(100,100));
        Assert.NotNull(new SearchRoi(RoiShape.Circle,[new(5,5),new(50,50)]).Validate(100,100));
        Assert.Null(new SearchRoi(RoiShape.Circle,[new(50,50),new(70,50)]).Validate(100,100));
        Assert.Null(new SearchRoi(RoiShape.Polygon,[new(10,10),new(80,10),new(80,80),new(10,80)]).Validate(100,100));
        Assert.NotNull(new SearchRoi(RoiShape.Rectangle,[new(double.NaN,0),new(100,100)]).Validate(100,100));
        Assert.NotNull(new SearchRoi(RoiShape.Rectangle,[new(10,10),new(10,10)]).Validate(100,100));
    }
    [Fact]public void SaveReloadRevisionAndDelete()
    {
        var store=new FileRecipeStore(root);var original=InspectionTests.Recipe() with{Revision=0};
        var saved=store.Save(original,[[1,2,3],[4,5,6]]);
        Assert.Equal(1,saved.Revision);Assert.Equal(2,store.LoadReference(saved,0)[1]);
        var loaded=Assert.Single(store.LoadAll());Assert.Equal("QK1.11",loaded.Ends[0].ExpectedLines[0]);
        var second=store.Save(loaded,[[7],[8]]);Assert.Equal(2,second.Revision);
        Assert.Throws<InvalidDataException>(()=>store.Save(loaded,[[9],[10]]));
        store.Delete(second.Id);Assert.Empty(store.LoadAll());
    }
    [Fact]public void Supports160ModelsWithoutHardcodedIds()
    {
        var store=new FileRecipeStore(root);
        for(var i=0;i<160;i++)store.Save(InspectionTests.Recipe() with{ModelCode=$"M{i:000}",Revision=0},[[1],[2]]);
        Assert.Equal(160,store.LoadAll().Count);
        Assert.Throws<InvalidDataException>(()=>store.Save(InspectionTests.Recipe() with{ModelCode="M001",Revision=0},[[1],[2]]));
    }
    [Fact]public void MalformedRecipeIsReportedAndNeverLoaded()
    {
        var dir=Path.Combine(root,"recipes","bad");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"recipe.json"),"{bad");
        var store=new FileRecipeStore(root);Assert.Empty(store.LoadAll());Assert.Single(store.LoadErrors);
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
