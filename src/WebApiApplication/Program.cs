using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Owin.Hosting;
using Owin;
using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;

namespace WebApiApplication
{
    class Program
    {
        static void Main(string[] args)
        {
            string baseAddress = "http://localhost:9000/";

            using (WebApp.Start<Startup>(url: baseAddress))
            {
                var client = new HttpClient();

                var response = client.GetAsync(baseAddress + "hello/test").Result;

                Console.WriteLine(response);
                Console.WriteLine(response.Content.ReadAsStringAsync().Result);
            }

            Console.ReadLine();
        }

        public static ConditionalWeakTable<HttpRequestMessage, string> Params = new ConditionalWeakTable<HttpRequestMessage, string>();

        public class Startup
        {
            public void Configuration(IAppBuilder app)
            {
                var containerBuilder = new ContainerBuilder();

                containerBuilder.RegisterApiControllers(typeof(Startup).Assembly);

                containerBuilder.RegisterType<MyContext>().InstancePerRequest();

                var container = containerBuilder.Build();

                var config = new HttpConfiguration();

                config.DependencyResolver = new AutofacWebApiDependencyResolver(container);

                config.MapHttpAttributeRoutes();

                config.Services.Add(typeof(IExceptionLogger), new Logger());

                config.EnsureInitialized();    

                app.UseWebApi(config);
            }
        }
    }

    public class MyContext
    {
        public string Param { get; set; }
    }

    public class Logger : ExceptionLogger
    {
        public override void Log(ExceptionLoggerContext context)
        {
            var di = context.Request.GetDependencyScope();

            var myContext = di.GetService(typeof(MyContext)) as MyContext;

            var owinContext = context.Request.GetOwinContext().Get<string>("myparam");

            string wct;

            Program.Params.TryGetValue(context.Request, out wct);

            Console.WriteLine("Parameter from DI: {0}", myContext.Param);
            Console.WriteLine("Parameter from OwinContext: {0}", owinContext);
            Console.WriteLine("Parameter from WCT: {0}", wct);
            Console.WriteLine("Parameter from Route Data: {0}", context.RequestContext.RouteData.Values["param"]);
        }
    }

    public class MyController : ApiController
    {
        MyContext _context;

        public MyController(MyContext context)
        {
            _context = context;
        }

        [Route("hello/{param}")]
        public IHttpActionResult Get(string param)
        {
            _context.Param = param;
            Request.GetOwinContext().Set("myparam", param);
            Program.Params.Add(Request, param);

            throw new Exception();
        }

    }
}
