const safeLookupString = (strings, group, key) => {
  if (!strings?.[group]?.[key]) {
    return `missing string: ${group}.${key}`;
  }
  return strings[group][key];
};

export { safeLookupString };
