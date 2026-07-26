import Component from "./feature-flags";
import mockFeatureFlagService from "../hooks/use-feature-flag/mock-feature-flag-service";

export default {
  title: "generic/feature-flags",
};

const flagOnService = mockFeatureFlagService({ flagResult: true });
const flagOffService = mockFeatureFlagService({ flagResult: false });
const errorService = mockFeatureFlagService({ loadSucceed: false });
const loadingService = mockFeatureFlagService({ loadDelay: -1 });

export const FlagTrueSimple = () => (
  <Component flag="moo" service={flagOnService}>
    {/*first child is what to show if flag is true */}
    <div>
      <strong>flag is on!</strong> yay
    </div>
  </Component>
);

export const FlagTrueWithLoading = () => (
  <Component flag="moo" service={flagOnService}>
    {/*first child is what to show if flag is true */}
    <div>
      <strong>flag is on!</strong> yay
    </div>
    {/*second child is what to show if flag is true */}
    <div>
      <strong>flag is off</strong> sad
    </div>
    {/*third child is what you use for loading state */}
    <div>...loading...</div>
  </Component>
);

export const FlagFalseSimple = () => (
  <Component flag="moo" service={flagOffService}>
    {/*first child is what to show if flag is true */}
    <p>hi</p>
    {/*second child is what to show if flag is true */}
    <div>
      <strong>flag is off</strong> sad
    </div>
  </Component>
);

export const FlagFalseWithLoading = () => (
  <Component flag="moo" service={flagOffService}>
    {/*first child is what to show if flag is true */}
    <div>
      <strong>flag is on!</strong> yay
    </div>
    {/*second child is what to show if flag is true */}
    <div>
      <strong>flag is off</strong> sad
    </div>
    {/*third child is what you use for loading state */}
    <div>...loading...</div>
  </Component>
);

export const LoadingState = () => (
  <Component flag="moo" service={loadingService}>
    {/*first child is what to show if flag is true */}
    <div>
      <strong>flag is on!</strong> yay
    </div>
    {/*second child is what to show if flag is true */}
    <div>
      <strong>flag is off</strong> sad
    </div>
    {/*third child is what you use for loading state */}
    <div>...loading...</div>
  </Component>
);

const Error = ({ clear }: { clear?: () => void }) => (
  <button onClick={clear}>error</button>
);

export const ErrorState = () => (
  <Component flag="moo" service={errorService}>
    {/*first child is what to show if flag is true */}
    <div>
      <strong>flag is on!</strong> yay
    </div>
    {/*second child is what to show if flag is true */}
    <div>
      <strong>flag is off</strong> sad
    </div>
    {/*third child is what you use for loading state */}
    <div>...loading...</div>
    {/*fourth child gives you an error state} */}
    <Error />
  </Component>
);
