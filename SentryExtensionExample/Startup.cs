using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;
using Sentry;

namespace SentryExtensionExample
{
    public class Startup
    {
        public static readonly LoggerFactory _myLoggerFactory =
            new LoggerFactory(new[]
            {
                new Microsoft.Extensions.Logging.Debug.DebugLoggerProvider()
            });

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
                .AddNewtonsoftJson(x => x.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore)
                .AddNewtonsoftJson(x => x.SerializerSettings.Converters.Add(new StringEnumConverter()));

            services.AddAutoMapper(typeof(Startup));

            services.AddCors(option =>
            {
                option.AddDefaultPolicy(
                    builder =>
                    {
                        builder
                        .AllowAnyOrigin()
                        .SetIsOriginAllowedToAllowWildcardSubdomains()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                    });
            });


            services.AddSignalR();
            services.AddHttpClient();
            services.AddHttpContextAccessor();

            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "API", Version = "v1" });
            });


            //initialize sentry sdk
            SentrySdk.Init(sentry =>
            {
                SentryOptions options = new SentryOptions();

                // Add this to the SDK initialization callback
                // To set a uniform sample rate
                options.TracesSampleRate = 1.0;

                options.TracesSampler = context =>
                {
                    // If this is the continuation of a trace, just use that decision (rate controlled by the caller)
                    if (context.TransactionContext.IsParentSampled is not null)
                    {
                        return context.TransactionContext.IsParentSampled.Value
                            ? 1.0
                            : 0.0;
                    }

                    // Otherwise, sample based on URL (exposed through custom sampling context)
                    return context.CustomSamplingContext.GetValueOrDefault("url") switch
                    {
                        // The websocket request endpoint is just noise - drop all transactions
                        "/api/notify" => 0.0,
                        "api/notify/negotiate" => 0.0,

                        // Or return null to fallback to options.TracesSampleRate (1.0 in this case)
                        _ => null
                    };
                };
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHttpContextAccessor httpContext)
        {

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                //app.UseDatabaseErrorPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            // Enable automatic tracing integration.
            // Make sure to put this middleware right after `UseRouting()`.
            app.UseSentryTracing();

            app.UseCors();//this was moved here since the BasicAuthMiddleware below is also authentication and cors must come before authentication.

            if (env.IsDevelopment() || env.IsStaging())
            {
                app.UseSwagger();
                app.UseSwaggerUI(opt =>
                {
                    opt.SwaggerEndpoint("/swagger/v1/swagger.json", "API V1");
                });
            }

            //TODO: Configure api to throw 400 if the calling client is not doing so from https; using .UseHttpsRedirection is suitable for web apps not api
            //https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-2.2&tabs=visual-studio

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
