import { Injectable } from '@angular/core';
import { Router, Event, NavigationEnd, ActivatedRouteSnapshot } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { ApplicationInsightsService } from './application-insights.service';

@Injectable({
  providedIn: 'root'
})
export class RouterEventHandlerService {

  constructor(private router: Router, private ai: ApplicationInsightsService, private title: Title) { }

  public start() {
    this.router.events.subscribe(this.handleRouterEvent.bind(this));
  }

  getRouteData() {
    const params: Record<string, string> = {};
    const data: Record<string, any> = {};
    let node: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    while (node) {
      for (const k of Object.keys(node.params)) {
        params[k] = node.params[k];
      }
      for (const k of Object.keys(node.data)) {
        data[k] = node.data[k];
      }
      node = node.firstChild;
    }
    return {params, data};
  }

  formatTitle({params, data}: ReturnType<typeof RouterEventHandlerService.prototype.getRouteData>): string {
    if (data.title) {
      if (typeof data.title === "string") {
        return data.title;
      }
      return data.title(params, data);
    }
    return "Nowhere";
  }

  formatName({params, data}: ReturnType<typeof RouterEventHandlerService.prototype.getRouteData>): string {
    if (data.name) {
      if (typeof data.name === "string") {
        return data.name;
      }
      return data.name(params, data);
    }
    return "Unknown";
  }

  handleRouterEvent(event: Event) {
    if(event instanceof NavigationEnd) {
      const routeData = this.getRouteData();
      const properties: Record<string, string> = {};
      for (const k of Object.keys(routeData.params)) {
        properties["route-" + k] = routeData.params[k];
      }

      this.title.setTitle(this.formatTitle(routeData) + " - Maestro++");
      this.ai.trackPageView({
        uri: event.urlAfterRedirects,
        properties: properties,
        name: this.formatName(routeData),
      });
    }
  }
}
