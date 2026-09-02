using System.IO;
using WireMarkerInspection.Desktop.ViewModels;
using WireMarkerInspection.Domain;
using WireMarkerInspection.Infrastructure;
using Xunit;

namespace WireMarkerInspection.Tests;

[Collection(DispatcherTestHost.Collection)]
public sealed class ModelWorkflowTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"wmi-model-flow-"+Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddSaveReloadEditAndDeleteUseOnePersistentIdentity()=>DispatcherTestHost.Sta(()=>
    {
        var vm=new MainViewModel(root){Confirm=_=>true}.AsAdmin();
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
    public void SelectingLibraryRowLoadsRecipeAndClearingSelectionLocksSetup()=>DispatcherTestHost.Sta(()=>
    {
        var author=new MainViewModel(root){Confirm=_=>true}.AsAdmin();
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

        var vm=new MainViewModel(root){Confirm=_=>true}.AsAdmin();
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
    public void DecliningToDiscardADraftKeepsItAndRestoresTheSelectionAfterTheSelectionChange()=>DispatcherTestHost.Sta(()=>
    {
        SaveOneModel();
        var questions=0;
        RecipeRow? selectedWhenAsked=null;
        var vm=new MainViewModel(root).AsAdmin();
        vm.Confirm=_=>{questions++;selectedWhenAsked=vm.SelectedModel;return false;};
        try
        {
            var row=Assert.Single(vm.Models);
            vm.NewModelCommand.Execute(new ModelIdentity("M-DRAFT","Unsaved draft"));
            Assert.True(vm.Dirty);
            Assert.Null(vm.SelectedModel);

            vm.SelectedModel=row;   // the library DataGrid writes its new selection back

            Assert.Equal(1,questions);
            Assert.Same(row,selectedWhenAsked);
            // The originating control is still inside its own selection change here. Restoring the
            // previous selection synchronously is what crashed the application, so it must not have
            // happened yet.
            Assert.Same(row,vm.SelectedModel);
            Assert.Contains("Giữ lại thay đổi chưa lưu",vm.Message);

            DispatcherTestHost.Pump(()=>vm.SelectedModel==null,TimeSpan.FromSeconds(5),"The declined selection was never restored.");

            Assert.Equal(1,questions);   // restoring must not ask again
            Assert.Equal("M-DRAFT",vm.ModelCode);
            Assert.Equal("Unsaved draft",vm.ModelName);
            Assert.True(vm.Dirty);
            Assert.True(vm.CanConfigureModel);
            Assert.False(vm.CanManageSelectedModel);
        }
        finally{vm.ShutdownAsync().GetAwaiter().GetResult();}
    });

    [Fact]
    public void AcceptingTheDiscardLoadsTheSelectedModelWithoutWaitingForTheDispatcher()=>DispatcherTestHost.Sta(()=>
    {
        SaveOneModel();
        var vm=new MainViewModel(root){Confirm=_=>true}.AsAdmin();
        try
        {
            var row=Assert.Single(vm.Models);
            vm.NewModelCommand.Execute(new ModelIdentity("M-DRAFT","Unsaved draft"));
            Assert.True(vm.Dirty);

            vm.SelectedModel=row;

            Assert.Same(row,vm.SelectedModel);
            Assert.Equal("M-SAVED",vm.ModelCode);
            Assert.False(vm.Dirty);
            Assert.True(vm.CanManageSelectedModel);
        }
        finally{vm.ShutdownAsync().GetAwaiter().GetResult();}
    });

    [Fact]
    public void IdentityValidationRejectsEmptyAndDuplicateCodes()=>DispatcherTestHost.Sta(()=>
    {
        var vm=new MainViewModel(root){Confirm=_=>true}.AsAdmin();
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

    private void SaveOneModel()
    {
        var author=new MainViewModel(root){Confirm=_=>true}.AsAdmin();
        try
        {
            author.NewModelCommand.Execute(new ModelIdentity("M-SAVED","Saved model"));
            ConfigureBothEnds(author);
            author.SaveRecipeCommand.Execute(null);
        }
        finally{author.ShutdownAsync().GetAwaiter().GetResult();}
    }

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


    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
