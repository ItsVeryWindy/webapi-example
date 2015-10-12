using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Owin.Hosting;
using Owin;
using System;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Dispatcher;
using System.Net.Http.Formatting;
using System.IO;
using System.Text;
using System.Web.Http.Filters;
using System.Web.Http.Controllers;
using Castle.DynamicProxy;
using Autofac.Extras.DynamicProxy2;

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

        public class Startup
        {
            public void Configuration(IAppBuilder app)
            {
                var containerBuilder = new ContainerBuilder();

                containerBuilder.RegisterApiControllers(typeof(Startup).Assembly);

                containerBuilder.RegisterType<MyContext>().InstancePerRequest();
                containerBuilder.RegisterType<Aspect>()
                    .As<IAspectCaller>()
                    .EnableInterfaceInterceptors()
                    .InterceptedBy(typeof(Interceptor));
                containerBuilder.RegisterType<Interceptor>();

                var container = containerBuilder.Build();

                var config = new HttpConfiguration();

                config.DependencyResolver = new AutofacWebApiDependencyResolver(container);

                config.Routes.MapHttpRoute(
                    "Default",
                    "hello/{param}",
                    new
                    {
                        controller = "My"
                    },
                    null,
                    new RouteSpecificMessageHandler
                {
                    InnerHandler = new HttpControllerDispatcher(config)
                });

                config.Formatters.Remove(config.Formatters.XmlFormatter);
                config.Formatters.Remove(config.Formatters.JsonFormatter);
                config.Formatters.Add(new CustomMediaFormatter());

                config.MessageHandlers.Add(new GlobalMessageHandler());
                config.Services.Add(typeof(IExceptionLogger), new Logger());

                config.EnsureInitialized();    

                app.UseWebApi(config);
            }
        }
    }

    public class GlobalMessageHandler : DelegatingHandler
    {
        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Console.WriteLine("Global Message Handler Executing!");

            var di = request.GetDependencyScope();

            var myContext = di.GetService(typeof(MyContext)) as MyContext;

            myContext.CorrelationId = request.GetCorrelationId();

            var response = await base.SendAsync(request, cancellationToken);

            Console.WriteLine("Global Message Handler Executed! {0} {1}", request.RequestUri, response.StatusCode);

            return response;
        }
    }

    public class RouteSpecificMessageHandler : DelegatingHandler
    {
        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Console.WriteLine("Route Specific Message Handler Executing!");

            var response = await base.SendAsync(request, cancellationToken);

            Console.WriteLine("Route Specific Message Handler Executed!");

            return response;
        }
    }

    public class CustomExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(HttpActionExecutedContext actionExecutedContext)
        {
            Console.WriteLine("Exception Filter!");

            base.OnException(actionExecutedContext);
        }
    }

    public class CustomActionFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            Console.WriteLine("Action Filter Executing!");

            base.OnActionExecuting(actionContext);
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            Console.WriteLine("Action Filter Executed!");

            base.OnActionExecuted(actionExecutedContext);
        }
    }

    public class MyContext
    {
        public string Param { get; set; }
        public Guid CorrelationId { get; set; }
    }

    public class Logger : ExceptionLogger
    {
        public override void Log(ExceptionLoggerContext context)
        {
            var di = context.Request.GetDependencyScope();

            var myContext = di.GetService(typeof(MyContext)) as MyContext;

            var owinContext = context.Request.GetOwinContext().Get<string>("myparam");

            Console.WriteLine("CorrelationId: {0}", myContext.CorrelationId);
            Console.WriteLine("Parameter from DI: {0}", myContext.Param);
            Console.WriteLine("Parameter from OwinContext: {0}", owinContext);
            Console.WriteLine("Parameter from WCT: {0}", context.Request.GetParam());
            Console.WriteLine("Parameter from Route Data: {0}", context.RequestContext.RouteData.Values["param"]);
        }
    }

    public class CustomMediaFormatter : JsonMediaTypeFormatter
    {
        public override object ReadFromStream(Type type, Stream readStream, Encoding effectiveEncoding, IFormatterLogger formatterLogger)
        {
            Console.WriteLine("Reading Json!");

            return base.ReadFromStream(type, readStream, effectiveEncoding, formatterLogger);
        }

        public override void WriteToStream(Type type, object value, Stream writeStream, Encoding effectiveEncoding)
        {
            Console.WriteLine("Writing Json!");

            base.WriteToStream(type, value, writeStream, effectiveEncoding);
        }
    }

    public class MyController : ApiController
    {
        MyContext _context;
        IAspectCaller _aspect;

        public MyController(MyContext context, IAspectCaller aspect)
        {
            _context = context;
            _aspect = aspect;
        }

        [CustomActionFilter]
        [CustomExceptionFilter]
        public IHttpActionResult Get(string param)
        {
            _context.Param = param;
            Request.GetOwinContext().Set("myparam", param);
            Request.SetParam(param);

            _aspect.CallMe();

            throw new Exception();
        }

    }

    public interface IAspectCaller
    {
        bool CallMe();
    }

    public class Aspect : IAspectCaller
    {
        public bool CallMe()
        {
            return true;
        }
    }

    public class Interceptor : IInterceptor
    {
        MyContext _context;

        public Interceptor(MyContext context)
        {
            _context = context;
        }

        public void Intercept(IInvocation invocation)
        {
            Console.WriteLine("Request Corelation Id: {0}", _context.CorrelationId);

            invocation.Proceed();
        }
    }

    public static class RequestExtensions
    {
        private static ConditionalWeakTable<HttpRequestMessage, string> _params = new ConditionalWeakTable<HttpRequestMessage, string>();

        public static void SetParam(this HttpRequestMessage request, string value)
        {
            _params.Add(request, value);
        }

        public static string GetParam(this HttpRequestMessage request)
        {
            string value;

            if (!_params.TryGetValue(request, out value))
                return null;

            return value;
        }
    }
}
