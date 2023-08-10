import { Injectable } from '@angular/core';
import { ApplicationInsights } from "@microsoft/applicationinsights-web";
import { Router, Event, NavigationEnd, ActivatedRouteSnapshot } from '@angular/router';

@Injectable({
  providedIn: 'root'
})
export class ApplicationInsightsService extends ApplicationInsights {
  constructor() {
    const aiConnectionString = (window as any).applicationData.aiConnectionString;
    if (aiConnectionString) {
      super({
        config: {
          connectionString: aiConnectionString,
          disableTelemetry: aiConnectionString == 'InstrumentationKey=00000000-0000-0000-0000-000000000000',
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
