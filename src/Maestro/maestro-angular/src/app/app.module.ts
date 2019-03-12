import { NgModule } from "@angular/core";
import { BrowserModule } from "@angular/platform-browser";
import { BrowserAnimationsModule } from "@angular/platform-browser/animations";
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
import { MomentModule } from "ngx-moment";

import { MaestroModule } from "src/maestro-client";
import { StatefulModule } from "src/stateful";

import { AppRoutingModule } from "./app-routing.module";
import { AppComponent } from "./app.component";
import { BuildComponent } from "./page/build/build.component";
import { MainComponent } from "./page/main/main.component";
import { SideBarChannelComponent } from "./widget/side-bar-channel/side-bar-channel.component";
import { SideBarComponent } from "./widget/side-bar/side-bar.component";

import { maestroOptions } from "src/environments/environment";
import { RecentBuildComponent } from './page/recent-build/recent-build.component';
import { BuildGraphTableComponent } from './page/build-graph-table/build-graph-table.component';
import { UriEncodePipe } from './uri-encode.pipe';

@NgModule({
  declarations: [
    AppComponent,
    MainComponent,
    SideBarComponent,
    SideBarChannelComponent,
    BuildComponent,
    RecentBuildComponent,
    BuildGraphTableComponent,
    UriEncodePipe,
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    NgbModule,
    FontAwesomeModule,
    BrowserAnimationsModule,
    NgbCollapseModule,
    MomentModule,
    MaestroModule.forRoot(maestroOptions),
    StatefulModule,
  ],
  providers: [],
  bootstrap: [AppComponent],
})
export class AppModule { }

library.add(
  faAngleDoubleUp,
  faAngleDown,
  faAngleRight,
  faAngleUp,
  faCheckCircle,
  faEllipsisV,
  faExclamation,
  faExclamationCircle,
  faExternalLinkAlt,
  faInfoCircle,
  faTimesCircle,
  faQuestionCircle,
  farQuestionCircle,
  faLock,
  faLockOpen,
);
