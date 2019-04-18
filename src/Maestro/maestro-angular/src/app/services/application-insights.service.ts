import { Injectable } from '@angular/core';
import { ApplicationInsights } from "@microsoft/applicationinsights-web";
import { Router, Event, NavigationEnd, ActivatedRouteSnapshot } from '@angular/router';

@Injectable({
  providedIn: 'root'
})
export class ApplicationInsightsService extends ApplicationInsights {
  constructor(private router: Router) {
    super({
      config: {
        instrumentationKey: (window as any).applicationData.aiKey,
        disableFetchTracking: false,
      },
    });
    this.loadAppInsights();


    this.router.events.subscribe(this.handleRouteChange.bind(this));
  }

  getRouteProperties() {
    const params: Record<string, string> = {};
    let node: ActivatedRouteSnapshot | null = this.router.routerState.snapshot.root;
    while (node) {
      for (const k of Object.keys(node.params)) {
        params["route-" + k] = node.params[k];
      }
      node = node.firstChild;
    }
    return params;
  }

  handleRouteChange(event: Event) {
    if (event instanceof NavigationEnd) {
      this.trackPageView({
        uri: event.urlAfterRedirects,
        properties: this.getRouteProperties(),
      });
    }
  }
}
