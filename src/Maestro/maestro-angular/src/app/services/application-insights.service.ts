import { Injectable } from '@angular/core';
import { ApplicationInsights } from "@microsoft/applicationinsights-web";
import { Router, Event, NavigationEnd, ActivatedRouteSnapshot } from '@angular/router';

@Injectable({
  providedIn: 'root'
})
export class ApplicationInsightsService extends ApplicationInsights {
  constructor() {
    const ikey = (window as any).applicationData.aiKey;
    if (ikey) {
      super({
        config: {
          instrumentationKey: ikey,
          disableTelemetry: ikey == '00000000-0000-0000-0000-000000000000',
          disableFetchTracking: false,
        },
      });
      this.loadAppInsights();
    }
  }

  public start() {
    // nothing needed
  }
}
