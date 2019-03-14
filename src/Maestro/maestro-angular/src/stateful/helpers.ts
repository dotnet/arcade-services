import { switchMap, catchError, concat, mergeMap } from 'rxjs/operators';
import { OperatorFunction, ObservableInput, of, Observable } from 'rxjs';

// Class that we can use to wrap error in observables that we can detect later
export class WrappedError {
  constructor(public error: any) {}
}

// Object that can be emitted to signal a reload
export class Loading {
}

export type StatefulResult<T> = T | WrappedError | Loading;

// Switch map that wraps the function in logic to support the "stateful" component
// Exactly like switchMap, this operator calls the project function every time the
// input observable emits a new value.
// For each input value 2 StatefulResult values will be output, one when the inner
// Observable is initially subscribed to signifying a "Loading" state, and a second
// when the observable finishes or errors signifying a "Loaded" or "Error" state
//
// Use the statefulPipe function to add additional operators that run against the
// "Loaded" values while passing the "Loading" and "Error" values through
export function statefulSwitchMap<T, R>(project: (value: T, index: number) => ObservableInput<R>): OperatorFunction<T, StatefulResult<R>> {
  return switchMap((v, i) => {
    return of(new Loading()).pipe(
      concat(project(v, i)),
      catchError(err => of(new WrappedError(err))),
    );
  });
}

export function statefulPipe<T, A>(op1: OperatorFunction<T, A>): OperatorFunction<StatefulResult<T>, StatefulResult<A>>;
export function statefulPipe<T, A, B>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>): OperatorFunction<StatefulResult<T>, StatefulResult<B>>;
export function statefulPipe<T, A, B, C>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>): OperatorFunction<StatefulResult<T>, StatefulResult<C>>;
export function statefulPipe<T, A, B, C, D>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>): OperatorFunction<StatefulResult<T>, StatefulResult<D>>;
export function statefulPipe<T, A, B, C, D, E>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>, op5: OperatorFunction<D, E>): OperatorFunction<StatefulResult<T>, StatefulResult<E>>;
export function statefulPipe<T, A, B, C, D, E, F>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>, op5: OperatorFunction<D, E>, op6: OperatorFunction<E, F>): OperatorFunction<StatefulResult<T>, StatefulResult<F>>;
export function statefulPipe<T, A, B, C, D, E, F, G>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>, op5: OperatorFunction<D, E>, op6: OperatorFunction<E, F>, op7: OperatorFunction<F, G>): OperatorFunction<StatefulResult<T>, StatefulResult<G>>;
export function statefulPipe<T, A, B, C, D, E, F, G, H>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>, op5: OperatorFunction<D, E>, op6: OperatorFunction<E, F>, op7: OperatorFunction<F, G>, op8: OperatorFunction<G, H>): OperatorFunction<StatefulResult<T>, StatefulResult<H>>;
export function statefulPipe<T, A, B, C, D, E, F, G, H, I>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>, op5: OperatorFunction<D, E>, op6: OperatorFunction<E, F>, op7: OperatorFunction<F, G>, op8: OperatorFunction<G, H>, op9: OperatorFunction<H, I>): OperatorFunction<StatefulResult<T>, StatefulResult<I>>;
export function statefulPipe<T, A, B, C, D, E, F, G, H, I>(op1: OperatorFunction<T, A>, op2: OperatorFunction<A, B>, op3: OperatorFunction<B, C>, op4: OperatorFunction<C, D>, op5: OperatorFunction<D, E>, op6: OperatorFunction<E, F>, op7: OperatorFunction<F, G>, op8: OperatorFunction<G, H>, op9: OperatorFunction<H, I>, ...operations: OperatorFunction<any, any>[]): OperatorFunction<StatefulResult<T>, any>;
export function statefulPipe<T>(...args: OperatorFunction<any, any>[]): OperatorFunction<StatefulResult<T>, any> {
  const predicate = function (value: any) {
    return value instanceof WrappedError || value instanceof Loading;
  }
  return function(source: Observable<StatefulResult<T>>) {
    return source.pipe(
      mergeMap(value => !predicate(value) ? (of(value).pipe as any)(...args) : of(value)),
    );
  }
}
