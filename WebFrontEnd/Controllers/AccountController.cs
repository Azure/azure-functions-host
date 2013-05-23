using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace WebFrontEnd.Controllers
{
    public class AccountController : Controller
    {
        //
        // GET: /Account/

        public ActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Login(LogOnViewModel model, string returnUrl)
        {
            if (ModelState.IsValid)
            {
                string expected = RoleEnvironment.GetConfigurationSettingValue("LoginPassword");

                bool f = (model.Password == expected);
                // bool f = Membership.ValidateUser(model.UserName, model.Password);
                if (!f)
                {
                    ModelState.AddModelError("", "Bad username or password");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(); // fail
            }

            FormsAuthentication.SetAuthCookie(model.UserName, createPersistentCookie: true);

            return Redirect(returnUrl ?? Url.Action("Index", "Home"));
        }

        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
