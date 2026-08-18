#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MBRevitDev_Hub.Buttons;
using System;
using System.Collections.Generic;

#endregion

namespace MBRevitDev_Hub
{
    internal class AddInApplication : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            RibbonBuilder.Build(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
