import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Subscription, Asset } from 'src/maestro-client/models';
import { Observable, of } from 'rxjs';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { StatefulResult} from 'src/stateful';
import { BuildService } from 'src/app/services/build.service';
import { VersionDetails } from 'src/app/model/version-details';
import { SubscriptionDependencyDetails } from 'src/app/model/subscription-dependency-details';

@Component({
  selector: 'mc-subscriptions-table',
  templateUrl: './subscriptions-table.component.html',
  styleUrls: ['./subscriptions-table.component.scss']
})
export class SubscriptionsTableComponent implements OnChanges {

  @Input() public rootId?: number;
  @Input() public subscriptionsList?: Record<string, Subscription[]>;
  @Input() public assets!: Asset[];
  @Input() public repository!: string;

  public openDetails = false;
  public openAssets = false;
  public openDependencies = false;
  public subscriptionDependencyDetailsList: Record<string, Observable<StatefulResult<SubscriptionDependencyDetails>>> = {};

  get branches() {
    return Object.keys(this.subscriptionsList || {});
  }

// Uses the DarcLib SubscriptionHealthMetrics to load additional details about the subscription usage for this repository
  loadVersionData() {
    if(this.subscriptionsList){
      for(const branch of this.branches){
        this.subscriptionDependencyDetailsList[branch] = SubscriptionDependencyDetails.retrieveDataFromServer(this.http, this.repository, branch)
      }
    }
  }

  constructor(private http: HttpClient, private buildService: BuildService) { }

  ngOnChanges(changes: SimpleChanges) {
    if ("subscriptionsList" in changes) {
      this.loadVersionData();
    }
  }
}
