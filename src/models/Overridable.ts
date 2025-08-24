import { OverrideBehaviour } from "./OverrideBehaviour";

export type Overridable<T> = T | {
    value: T;
    behaviour: OverrideBehaviour;
};
