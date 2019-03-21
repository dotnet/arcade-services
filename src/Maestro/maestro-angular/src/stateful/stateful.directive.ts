import { ComponentFactoryResolver, EmbeddedViewRef, Directive, DoCheck, TemplateRef, ViewContainerRef, Input, Type, ComponentRef, OnDestroy, Output, EventEmitter } from "@angular/core";
import { Observable, SubscriptionLike } from 'rxjs';
import { GenericErrorComponent } from './generic-error/generic-error.component';
import { ProgressRingComponent } from './progress-ring/progress-ring.component';
import { WrappedError, Loading } from './helpers';

export class StatefulContext<T> {
  constructor(public $implicit: T) {
  }
}

export class StatefulErrorContext {
  error: any;
  constructor(public $implicit: any) {
    this.error = $implicit;
  }
}

export interface View<T> {
  updateContext(newValues: T): void;
  destroy(): void;
}

export function View<T>(componentOrViewRef: ComponentRef<T> | EmbeddedViewRef<T>): View<T> {
  if (componentOrViewRef instanceof ComponentRef) {
    return new ComponentRefView(componentOrViewRef);
  } else {
    return new ViewRefView(componentOrViewRef);
  }
}

class ViewRefView<T> implements View<T> {
  constructor(private viewRef: EmbeddedViewRef<T>) {
  }

  public updateContext(newValues: T) {
    Object.assign(this.viewRef.context, newValues);
  }

  public destroy() {
    this.viewRef.destroy();
  }
}

class ComponentRefView<T> implements View<T> {

  constructor(private componentRef: ComponentRef<T>) {
  }

  public updateContext(newValues: T) {
    Object.assign(this.componentRef.instance, newValues);
  }

  public destroy() {
    this.componentRef.destroy();
  }
}

export interface Kind {
  errorComponent: Type<any>;
  loadingComponent: Type<any>;
}

const kinds: Record<string, Kind> = {
  general: {
    errorComponent: GenericErrorComponent,
    loadingComponent: ProgressRingComponent,
  }
};

interface Subscriber {
  subscribe(value: Observable<any> | Promise<any>, valueCallback: (v: any) => void, errorCallback: (e: any) => void): SubscriptionLike | Promise<any>;
  destroy(subscription: SubscriptionLike | Promise<any>): void;
}

class ObservableSubscriber implements Subscriber {
  static instance = new ObservableSubscriber();

  subscribe(value: Observable<any>, valueCallback: (v: any) => void, errorCallback: (e: any) => void): SubscriptionLike {
    return value.subscribe({
      next: valueCallback,
      error: errorCallback,
    });
  }

  destroy(subscription: SubscriptionLike): void {
    subscription.unsubscribe();
  }
}

class PromiseSubscriber implements Subscriber {
  static instance = new PromiseSubscriber();

  subscribe(value: Promise<any>, valueCallback: (v: any) => void, errorCallback: (e: any) => void): Promise<any> {
    return value.then(valueCallback, errorCallback);
  }

  destroy(subscription: Promise<any>): void {

  }
}

@Directive({
  selector: "[stateful][statefulFrom]",
})
export class StatefulDirective<T> implements DoCheck, OnDestroy {
  private loadingView?: View<any>;
  private errorView?: View<StatefulErrorContext>;
  private loadedView?: View<StatefulContext<T>>;

  private _value?: T = undefined;
  private _error?: any = undefined;

  private _subscriber?: Subscriber;
  private _subscription?: SubscriptionLike | Promise<any>;

  @Output("statefulState") public state = new EventEmitter<string>();
  @Input("statefulKind") public kind: string = "general";
  @Input("statefulErrorTemplate") public errorTemplate?: TemplateRef<StatefulErrorContext>;
  @Input("statefulLoadingTemplate") public loadingTemplate?: TemplateRef<{}>;

  private _currentAsyncValue?: Promise<T> | Observable<T>;
  @Input("statefulFrom") set asyncValue(promiseOrObservable: Promise<T> | Observable<T>) {
    if (this._currentAsyncValue === promiseOrObservable) {
      return;
    }
    this._currentAsyncValue = promiseOrObservable;
    this.destroy();

    if ('subscribe' in promiseOrObservable) {
      this._subscriber = ObservableSubscriber.instance;
    } else {
      this._subscriber = PromiseSubscriber.instance;
    }
    this._value = undefined;
    this._error = undefined;
    this.state.emit("loading");
    this._subscription = this._subscriber.subscribe(promiseOrObservable, r => {
      if (r instanceof WrappedError) {
        this._value = undefined;
        this._error = r.error;
        this.state.emit("error");
      } else if (r instanceof Loading) {
        this._value = undefined;
        this._error = undefined;
        this.state.emit("loading");
      } else {
        this._value = r;
        this._error = undefined;
        this.state.emit("loaded");
      }
    }, e => {
      this._value = undefined;
      this._error = e;
    });
  }

  get loading() {
    return this._value === undefined &&
      this._error === undefined;
  }

  get loaded() {
    return this._value !== undefined;
  }

  get errored() {
    return this._error !== undefined;
  }

  get value() {
    return this._value;
  }

  get error() {
    return this._error;
  }

  get errorComponent() {
    return kinds[this.kind].errorComponent;
  }

  get loadingComponent() {
    return kinds[this.kind].loadingComponent;
  }

  constructor(
    private templateRef: TemplateRef<StatefulContext<T>>,
    private viewContainer: ViewContainerRef,
    private componentFactoryResolver: ComponentFactoryResolver
  ) {
  }

  updateErrorView() {
    if (!this.errored) {
      if (this.errorView) {
        this.errorView.destroy();
        this.errorView = undefined;
      }
    } else {
      if (this.errorView) {
        this.errorView.updateContext({
          $implicit: this.error,
          error: this.error,
        });
      } else {
        this.errorView = this.renderComponentOrTemplate(this.errorComponent, this.errorTemplate, new StatefulErrorContext(this.error));
      }
    }
  }

  updateLoadingView() {
    if (!this.loading) {
      if (this.loadingView) {
        this.loadingView.destroy();
        this.loadingView = undefined;
      }
    } else {
      if (!this.loadingView) {
        this.loadingView = this.renderComponentOrTemplate(this.loadingComponent, this.loadingTemplate, {});
      }
    }
  }

  updateView() {
    if (!this.loaded) {
      if (this.loadedView) {
        this.loadedView.destroy();
        this.loadedView = undefined;
      }
    } else {
      if (this.loadedView) {
        this.loadedView.updateContext({
          $implicit: this.value as any,
        });
      } else {
        this.loadedView = this.renderComponentOrTemplate(undefined, this.templateRef, new StatefulContext(this.value));
      }
    }
  }

  renderComponentOrTemplate<T>(component: Type<any> | undefined, template: TemplateRef<T> | undefined, context: T): View<T> {
    if (template) {
      return View(this.viewContainer.createEmbeddedView(template, context));
    } else if (component) {
      const factory = this.componentFactoryResolver.resolveComponentFactory(component);
      const componentRef = this.viewContainer.createComponent(factory);
      const view = View(componentRef);
      view.updateContext(context);
      return view;
    }
    throw new Error("Component or Template not set.");
  }

  ngDoCheck(): void {
    this.updateView();
    this.updateErrorView();
    this.updateLoadingView();
  }

  ngOnDestroy(): void {
    this.destroy();
  }

  destroy() {
    if (this._subscription && this._subscriber) {
      this._subscriber.destroy(this._subscription);
    }
  }
}
