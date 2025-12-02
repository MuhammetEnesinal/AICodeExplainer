using AICodeExplainer.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace AICodeExplainer.Controllers
{
    public class HomeController : Controller
    {
        private readonly GeminiService _geminiService;

        public HomeController(GeminiService geminiService)
        {
            _geminiService = geminiService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExplainCode(string userCode)
        {
            if (string.IsNullOrWhiteSpace(userCode))
            {
                ViewBag.Result = "Lütfen kod girin.";
                return View("Index");
            }

            try
            {
                string explanation = await _geminiService.ExplainCodeAsync(userCode);

                ViewBag.UserCode = userCode;
                ViewBag.Result = explanation;

                return View("Index");
            }
            catch (Exception ex)
            {
                ViewBag.Result = "Hata oluştu: " + ex.Message;
                return View("Index");
            }
        }
    }
}
