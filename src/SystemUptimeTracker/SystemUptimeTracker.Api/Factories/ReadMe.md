# Factories

Factories primary responsibility is to convert data to and from models we load and uiModels we present. They are not necessarily just dumb converters, they can also contain some application logic. 
For example, you could have a convert for save where the fields updated depend on permissions. The factory would be the right place to own that logic.
Another example could be something like the UI having a rule to only show stories in an "Approved" state, while the data model includes all workflow states. It would be appropriate to do that filtering in the factory.

## Repository Use

It can be appropriate to use repositories in a factory, but it should be carefully thought out. When done correctly, it keeps services from having to depend on tons of repositories that are only needed to support the factory. 
An example of a potentially appropriate repository in a factory could be with an address factory. The service may have a data model with a country id, but the UI wants the country name. The factory could be responsible for resolving that country id to the data it needs.

**note:** As a general rule of thumb, factories should typically call repositories that use caching to avoid multiple calls to the same data source.

## Folder Structure

Generally speaking, factories focus on UiModels over datamodels. We encourage ToUi and ToSubmit methods on a factory. Use folders to keep them organized. 
Any folder should have an Interfaces folder, and dependency injection is resolved in the Dependency Injection folder at the root of the Factories folder.



