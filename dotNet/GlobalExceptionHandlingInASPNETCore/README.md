# Global Exception Handling in ASP.NET Core – Ultimate Guide

## UseExceptionHandler Middleware comes out of the box with ASP.NET Core applications.

```csharp
app.UseExceptionHandler(
    options =>
    {
        options.Run(async context =>
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "text/html";
                var exceptionObject = context.Features.Get<IExceptionHandlerFeature>();
                if (null != exceptionObject)
                {
                    var errorMessage = $"{exceptionObject.Error.Message}";
                    await context.Response.WriteAsync(errorMessage).ConfigureAwait(false);
            }});
    }
);
```

> This is a very basic setup & usage of UseExceptionHandler Middleware. So, whenever there is an exception that is detected within the Pipeline of the application, the control falls back to this middleware, which in return will send a custom response to the request sender.

## Custom Middleware – Global Exception Handling In ASP.NET Core
Custom Global Exception Handling Middleware – Firstly, what is it? It’s a piece of code that can be configured as a middleware in the ASP.NET Core pipeline which contains our custom error handling logics. There are a variety of exceptions that can be caught by this pipeline.

### Creaglobal api response
But before that, let’s build a Response class that I recommend to be a part of every project you build, at least the concept. So, the idea is to make your ASP.NET Core API send uniform responses no matter what kind of requests it gets hit with. This make the work easier for whoever is consuming your API. Additionally it gives a much experience while developing.

```csharp
public class ApiResponse<T>
{
    public T Data { get; set; }
    public bool Succeeded { get; set; }
    public string Message { get; set; }
    public static ApiResponse<T> Fail(string errorMessage)
    {
        return new ApiResponse<T> { Succeeded = false, Message = errorMessage };
    }
    public static ApiResponse<T> Success(T data)
    {
        return new ApiResponse<T> { Succeeded = true, Data = data };
    }
}
```
The ApiResponse class is of a generic type, meaning any kind of data can be passed along with it. Data property will hold the actual data returned from the server. Message contains any Exceptions or Info message in string type. And finally there is a boolean that denotes if the request is a success. You can add multiple other properties as well depending on your requirement.

We also have Fail and Success method that is built specifically for our Exception handling scenario. You can find how this is being used in the upcoming sections.

### Create a custom exception
inherit Exception as the base class. Here is how the custom exception looks like
```csharp
public class SomeException : Exception
{
    public SomeException() : base()
    {
    }
    public SomeException(string message) : base(message)
    {
    }
    public SomeException(string message, params object[] args) : base(String.Format(CultureInfo.CurrentCulture, message, args))
    {
    }
}
```
Get the idea, right? In this way you can actually differentiate between exceptions. To get even more clarity related to this scenario, let’s say we have other custom exceptions like ProductNotFoundException , StockExpiredException, CustomerInvalidException and so on. Just give some meaningful names so that you can easily identify. Now you can use these exception classes wherever the specific exception arises. This sends the related exception to the middleware, which has logics to handle it.

## Create the Global Exception Handling Middleware


```csharp
public class ErrorHandlerMiddleware
{
    private readonly RequestDelegate _next;
    public ErrorHandlerMiddleware(RequestDelegate next)
    {
        _next = next;
    }
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception error)
        {
            var response = context.Response;
            response.ContentType = "application/json";
            var responseModel = ApiResponse<string>.Fail(error.Message);
            switch (error)
            {
                case SomeException e:
                    // custom application error
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    break;
                case KeyNotFoundException e:
                    // not found error
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    break;
                default:
                    // unhandled error
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    break;
            }
            var result = JsonSerializer.Serialize(responseModel);
            await response.WriteAsync(result);
        }
    }
}
```
Line 3 – RequestDelegate denotes a HTTP Request completion.
Line 10 – A simple try-catch block over the request delegate. It means that whenever there is an exception of any type in the pipeline for the current request, control goes to the catch block. In this middleware, Catch block has all the goodness.

Line 14 – Catches all the Exceptions. Remember, all our custom exceptions are derived from the Exception base class.
Line 18 – Creates an APIReponse Model out of the error message using the Fail method that we created earlier.
Line 21 – In case the caught exception is of type SomeException, the status code is set to BadRequest. You get the idea, yeah? The other exceptions are also handled in a similar fashion.
Line 34 – Finally, the created api-response model is serialized and send as a response.

### Reproduce the exception
```csharp
        [HttpGet]
        public IActionResult Get()
        {
            throw new Exception("An error occured...");
        }
```

with custom exception
```csharp
 [HttpGet]
        public IActionResult Get()
        {
            throw new SomeException("An error occured...");
        }
```

make sure that you don’t miss adding this middleware to the application pipeline. 

```csharp
app.UseMiddleware<ErrorHandlerMiddleware>();
```


















































































