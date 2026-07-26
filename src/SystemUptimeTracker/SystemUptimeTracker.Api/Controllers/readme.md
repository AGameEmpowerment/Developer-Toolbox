# Controllers

We want controllers implementation to be really simple. They generally call a service method and return the response. Try to avoid doing more in the controller. Most of the time, anything more belongs in the service.

## Base Api Controller

We will have a base controller that all controllers will inherit from. You can thus share functionality for signed in user, impersonation, permissions etc.

## Folder Structure

Group future controllers by cohesive application capability when multiple endpoints describe the same bounded area of the API.
You may then have a folder for Meteorologists with various controllers relating to dealing with meteorologists. The point being we want to strongly discourage having a single folder with all controllers in it.

## Http Files

HTTP files are used to document and test API controllers by providing a clear and concise way to define HTTP requests and expected responses. These files typically contain sample requests, including headers, query parameters, and body content, which can be executed directly within an IDE like Visual Studio or VS Code. This allows developers to quickly test endpoints and verify their behavior without needing a separate client or additional setup. HTTP files also serve as living documentation, making it easier for team members to understand how to interact with the API and ensuring that the API's functionality is well-documented and easily accessible. By using HTTP files, teams can streamline their development and testing processes, improve collaboration, and maintain up-to-date documentation.

Lastly, please make use of http files because they are cool.

For more information see [Http Files](https://learn.microsoft.com/en-us/aspnet/core/test/http-files)
