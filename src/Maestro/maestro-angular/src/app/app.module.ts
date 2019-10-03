import { NgModule, APP_INITIALIZER, Injector, Type } from "@angular/core";
import { BrowserModule } from "@angular/platform-browser";
import { BrowserAnimationsModule } from "@angular/platform-browser/animations";
import { FormsModule } from '@angular/forms';
import { FontAwesomeModule } from "@fortawesome/angular-fontawesome";
import { library } from "@fortawesome/fontawesome-svg-core";
import {
  faAngleDoubleUp,
  faAngleDown,
  faAngleRight,
  faAngleUp,
  faCheckCircle,
  faEllipsisV,
  faExclamation,
  faExclamationTriangle,
  faExclamationCircle,
  faExternalLinkAlt,
  faInfoCircle,
  faTimesCircle,
  faQuestionCircle,
  faLock,
  faLockOpen,
} from "@fortawesome/free-solid-svg-icons";
import {
  faQuestionCircle as farQuestionCircle,
} from "@fortawesome/free-regular-svg-icons";
import { NgbCollapseModule, NgbModule } from "@ng-bootstrap/ng-bootstrap";
import { NgxPaginationModule } from "ngx-pagination";

import { MaestroModule } from "src/maestro-client";
import { StatefulModule } from "src/stateful";

import { AppRoutingModule } from "./app-routing.module";
import { AppComponent } from "./app.component";
import { BuildComponent } from "./page/build/build.component";
import { MainComponent } from "./page/main/main.component";
import { SideBarChannelComponent } from "./widget/side-bar-channel/side-bar-channel.component";
import { SideBarComponent } from "./widget/side-bar/side-bar.component";

import { maestroOptions } from "src/environments/environment";
import { BuildGraphTableComponent } from './page/build-graph-table/build-graph-table.component';
import { UriEncodePipe } from './uri-encode.pipe';
import { SwitchComponent } from './widget/switch/switch.component';
import { TimeAgoComponent } from './widget/time-ago/time-ago.component';
import { RelativeDateComponent } from './widget/relative-date/relative-date.component';
import { CommitLinkPipe } from './pipes/commit-link.pipe';
import { BuildLinkPipe } from './pipes/build-link.pipe';
import { TreeViewModule } from 'src/tree-view';
import { BuildGraphTreeComponent } from './page/build-graph-tree/build-graph-tree.component';
import { RepoNamePipe } from './pipes/repo-name.pipe';
import { GetRepositoryNamePipe } from './pipes/get-repository-name.pipe';
import { AssetTableComponent } from './page/asset-table/asset-table.component';
import { ApplicationInsightsService } from './services/application-insights.service';
import { RouterEventHandlerService } from './services/router-event-handler.service';
import { SubscriptionsTableComponent } from './page/subscriptions-table/subscriptions-table.component';

@NgModule({
  declarations: [
    AppComponent,
    MainComponent,
    SideBarComponent,
    SideBarChannelComponent,
    BuildComponent,
    BuildGraphTableComponent,
    UriEncodePipe,
    SwitchComponent,
    TimeAgoComponent,
    RelativeDateComponent,
    CommitLinkPipe,
    BuildLinkPipe,
    BuildGraphTreeComponent,
    RepoNamePipe,
    GetRepositoryNamePipe,
    AssetTableComponent,
    SubscriptionsTableComponent,
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    NgbModule,
    FontAwesomeModule,
    BrowserAnimationsModule,
    NgbCollapseModule,
    MaestroModule.forRoot(maestroOptions),
    StatefulModule,
    FormsModule,
    TreeViewModule,
    NgxPaginationModule,
  ],
  providers: [
    {
      provide: APP_INITIALIZER,
      useFactory: initializerFactory,
      deps: [Injector],
      multi: true,
    },
  ],
  bootstrap: [AppComponent],
})
export class AppModule { }

export function initializerFactory(injector: Injector) {
  return function initializer() {
    injector.get<ApplicationInsightsService>(ApplicationInsightsService).start();
    injector.get<RouterEventHandlerService>(RouterEventHandlerService).start();
  }
}

library.add(
  faAngleDoubleUp,
  faAngleDown,
  faAngleRight,
  faAngleUp,
  faCheckCircle,
  faEllipsisV,
  faExclamation,
  faExclamationTriangle,
  faExclamationCircle,
  faExternalLinkAlt,
  faInfoCircle,
  faTimesCircle,
  faQuestionCircle,
  farQuestionCircle,
  faLock,
  faLockOpen,
);
