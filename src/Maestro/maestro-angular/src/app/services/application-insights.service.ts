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
  }

  public start() {
    // nothing needed
  }
}
