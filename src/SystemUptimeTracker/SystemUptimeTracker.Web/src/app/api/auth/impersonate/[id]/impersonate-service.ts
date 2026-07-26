const canImpersonateForIdentifier = async (identifier) => {
  /*This is where you can make a request to your application's
    api giving that identifier (be it a user id, username, etc depending on your app).
    You can have whatever your api needs to return, but ultimately this service should return true or false
    indicating if they can impersonate.
    This is just a stupid stub for the template project.

    */

  const canImpersonate = identifier !== "forbidden";
  /*If you want to know details about who you are impersonating, you could include that in the object returned here
    That data will be put in the impersonate-meta cookie eg (acting-as-meta would be the default cookie name)*/
  return {
    canImpersonate,
    data: {
      accountId: "12346",
      identifier: identifier,
      displayName: "Dummy Display Name",
    },
  };
};

export { canImpersonateForIdentifier };
