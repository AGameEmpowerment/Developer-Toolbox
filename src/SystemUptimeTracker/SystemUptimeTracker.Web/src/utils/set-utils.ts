//an array can be converted into a set and then these operations are available.
//using sets where possible is preferable because they are more performant for this kind of calculation.
//the caveat (often a nifty benefit) is that creating a set from an array is a way to basically do a .distinct operation.
//these functions are intended for arrays of simple value types not objects.  So numbers work, dates don't.

export function isSuperset(set, subset) {
  for (const elem of subset) {
    if (!set.has(elem)) {
      return false;
    }
  }
  return true;
}

export function union(setA, setB) {
  const _union = new Set(setA);
  for (const elem of setB) {
    _union.add(elem);
  }
  return _union;
}

export function intersection(setA, setB) {
  const _intersection = new Set();
  for (const elem of setB) {
    if (setA.has(elem)) {
      _intersection.add(elem);
    }
  }
  return _intersection;
}

export function symmetricDifference(setA, setB) {
  const _difference = new Set(setA);
  for (const elem of setB) {
    if (_difference.has(elem)) {
      _difference.delete(elem);
    } else {
      _difference.add(elem);
    }
  }
  return _difference;
}

export function difference(setA, setB) {
  const _difference = new Set(setA);
  for (const elem of setB) {
    _difference.delete(elem);
  }
  return _difference;
}

export function arrayGroupBy(arrayToGroup, key) {
  return arrayToGroup.reduce(function (rv, x) {
    const newRv = { ...rv };
    (newRv[x[key]] = newRv[x[key]] || []).push(x);
    return newRv;
  }, {});
}
