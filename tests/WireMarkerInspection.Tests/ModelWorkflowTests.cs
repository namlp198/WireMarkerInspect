using System.Runtime.ExceptionServices;
using System.IO;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;

namespace WireMarkerInspection.Tests;

public sealed class ModelWorkflowTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-model-flow-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddSaveReloadEditAndDeleteUseOnePersistentIdentity()=>Sta(()=>
    {
        var vm=new MainViewModel(root){Confirm=_=>true};
        try
        {
            Assert.False(vm.CanConfigureModel);
            Assert.False(vm.CanSaveRecipe);
            Assert.False(vm.CanManageSelectedModel);
            vm.NewModelCommand.Execute(new ModelIdentity(" M-001 "," Model One "));
            Assert.Equal("M-001",vm.ModelCode);
            Assert.Equal("Model One",vm.ModelName);
            Assert.True(vm.Dirty);
            Assert.True(vm.CanConfigureModel);
            Assert.True(vm.CanSaveRecipe);
            Assert.False(vm.CanManageSelectedModel);
            ConfigureBothEnds(vm);
            vm.SaveRecipeCommand.Execute(null);

            var first=Assert.Single(vm.Models);
            Assert.Equal("M-001",first.Code);
            Assert.Equal("Model One",first.Name);
            Assert.Equal("v1",first.Revision);
            Assert.False(vm.Dirty);
            Assert.False(vm.CanSaveRecipe);
            Assert.Equal(first,vm.SelectedModel);

            vm.EditModelCommand.Execute(new ModelIdentity("M-001A","Renamed Model"));
            Assert.True(vm.Dirty);
            Assert.True(vm.CanSaveRecipe);
            Assert.Equal("M-001A",vm.ModelCode);
            vm.SaveRecipeCommand.Execute(null);

            var edited=Assert.Single(new FileRecipeStore(root).LoadAll());
            Assert.Equal(first.Recipe.Id,edited.Id);
            Assert.Equal(2,edited.Revision);
            Assert.Equal("M-001A",edited.ModelCode);
            Assert.Equal("Renamed Model",edited.Name);

            vm.DeleteModelCommand.Execute(null);
            Assert.Empty(vm.Models);
            Assert.Empty(new FileRecipeStore(root).LoadAll());
            Assert.False(vm.CanConfigureModel);
            Assert.False(vm.CanSaveRecipe);
            Assert.False(vm.CanManageSelectedModel);
        }
        finally{vm.ShutdownAsync().GetAwaiter().GetResult();}
    });

    [Fact]
    public void SelectingLibraryRowLoadsRecipeAndClearingSelectionLocksSetup()=>Sta(()=>
    {
        var author=new MainViewModel(root){Confirm=_=>true};
        try
        {
            author.NewModelCommand.Execute(new ModelIdentity("M-LOAD","Load on selection"));
            ConfigureBothEnds(author);
            author.End1.ExpectedText="END-1\nA.01";
            author.End1.Apply();
            author.End2.ExpectedText="END-2/02";
            author.End2.Apply();
            author.SaveRecipeCommand.Execute(null);
        }
        finally{author.ShutdownAsync().GetAwaiter().GetResult();}

        var vm=new MainViewModel(root){Confirm=_=>true};
        try
        {
            Assert.Null(vm.SelectedModel);
            Assert.False(vm.CanConfigureModel);
            Assert.False(vm.CanSaveRecipe);
            var row=Assert.Single(vm.Models);
            Assert.Equal("END-1\nA.01",row.FirstExpected);
            Assert.Equal("END-2/02",row.SecondExpected);

            vm.SelectedModel=row;

            Assert.True(vm.CanConfigureModel);
            Assert.False(vm.CanSaveRecipe);
            Assert.True(vm.CanManageSelectedModel);
            Assert.Equal("M-LOAD",vm.ModelCode);
            Assert.Equal("Load on selection",vm.ModelName);
            Assert.NotNull(vm.End1.Image);
            Assert.NotNull(vm.End2.Image);
            Assert.Equal("END-1\nA.01",vm.End1.ExpectedText);
            Assert.Equal("END-2/02",vm.End2.ExpectedText);

            vm.SelectedModel=null;

            Assert.False(vm.CanConfigureModel);
            Assert.False(vm.CanSaveRecipe);
            Assert.False(vm.CanManageSelectedModel);
            Assert.Null(vm.End1.Image);
            Assert.Null(vm.End2.Image);
        }
        finally{vm.ShutdownAsync().GetAwaiter().GetResult();}
    });

    [Fact]
    public void IdentityValidationRejectsEmptyAndDuplicateCodes()=>Sta(()=>
    {
        var vm=new MainViewModel(root){Confirm=_=>true};
        try
        {
            Assert.NotNull(vm.ValidateModelIdentity(new("","Name")));
            Assert.NotNull(vm.ValidateModelIdentity(new("M1","")));
            vm.NewModelCommand.Execute(new ModelIdentity("M1","First"));
            ConfigureBothEnds(vm);vm.SaveRecipeCommand.Execute(null);
            Assert.Contains("đã tồn tại",vm.ValidateModelIdentity(new("m1","Second"))!);
            Assert.Null(vm.ValidateModelIdentity(new("m1","Renamed"),vm.SelectedModel!.Recipe.Id));
        }
        finally{vm.ShutdownAsync().GetAwaiter().GetResult();}
    });

    private static void ConfigureBothEnds(MainViewModel vm)
    {
        foreach(var editor in new[]{vm.End1,vm.End2})
        {
            editor.SetFrame(InspectionTests.Frame());
            editor.Roi=SearchRoi.FullImage(100,100);
            editor.ExpectedText="QK1.11/FT3.f";
            editor.Orientation=TextOrientation.Auto;
            editor.Apply();
        }
    }

    private static void Sta(Action action)
    {
        Exception? failure=null;
        var thread=new Thread(()=>{try{action();}catch(Exception ex){failure=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
