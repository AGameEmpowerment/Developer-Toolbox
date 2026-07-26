# Generic Components

## client-only

This is a component to generalize a solution to the nextjs hydration errors when a client component's first render would look different than the blank render you get from server renders.

## feature-flags

The feauture flags component uses the use-feature-flag hook and will conditionally render one of it's possible four children based on feature flag status. It allows you to quickly wrap existing jsx in a feature flag.

<FeatureFlags flag="bob">
<p>I'm the child that renders when flag is true</p>
<p>I'm the child that renders when the flag is false. (optional)</p>
<p>I'm the child that renders when the flag status is loading. (optional)</p>
<p>I'm the child that renders if we fail to evaluate flag status. (optional)</p>

**note:** Be careful when wrapping existing functionality in a flag to ensure that your functionality for true is a **single** jsx element. You may need to do <FeautreFLags flag="bob"><>your stuff here </></FeatureFlags>

For a full explanation of feature flag implementations, see the read me inside the hooks folder. Or, if the template hasn't been built out, you can see it documented on the page.js that has most of the home page's copy in it.
