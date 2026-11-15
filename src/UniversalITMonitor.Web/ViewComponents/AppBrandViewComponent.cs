using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.ViewComponents;

public sealed class AppBrandViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(AppBrandViewModel model) => View(model);
}
