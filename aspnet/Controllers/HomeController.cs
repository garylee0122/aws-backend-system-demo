using Microsoft.AspNetCore.Mvc;

namespace DemoAPI.Controllers;

public class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
